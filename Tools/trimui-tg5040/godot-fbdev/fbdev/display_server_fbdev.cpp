/**************************************************************************/
/*  display_server_fbdev.cpp                                              */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             GODOT ENGINE                               */
/**************************************************************************/

#include "display_server_fbdev.h"

#ifdef FBDEV_ENABLED

#include "gl_manager_fbdev.h"

#include "core/input/input.h"
#include "core/os/os.h"
#include "drivers/gles3/rasterizer_gles3.h"

static bool fbdev_debug_enabled() {
	static int cached = -1;
	if (cached < 0) {
		const char *value = getenv("GODOT_FBDEV_DEBUG");
		cached = (value != nullptr && value[0] == '1') ? 1 : 0;
	}
	return cached == 1;
}

#define FBDEV_TRACE(msg)                                     \
	do {                                                     \
		if (fbdev_debug_enabled()) {                         \
			fprintf(stderr, "fbdev-trace: %s\n", msg);       \
			fflush(stderr);                                  \
		}                                                    \
	} while (0)

#include <fcntl.h>
#include <linux/input.h>
#include <poll.h>
#include <sys/ioctl.h>
#include <unistd.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

const DisplayServerFbdev::KeyMap DisplayServerFbdev::key_map[] = {
	// Keyboard-ish key codes (sunxi-keyboard, uinput d-pads).
	{ KEY_UP, Key::UP },
	{ KEY_DOWN, Key::DOWN },
	{ KEY_LEFT, Key::LEFT },
	{ KEY_RIGHT, Key::RIGHT },
	{ KEY_ENTER, Key::ENTER },
	{ KEY_KPENTER, Key::KP_ENTER },
	{ KEY_ESC, Key::ESCAPE },
	{ KEY_SPACE, Key::SPACE },
	{ KEY_TAB, Key::TAB },
	{ KEY_BACKSPACE, Key::BACKSPACE },
	{ KEY_Q, Key::Q },
	{ KEY_E, Key::E },
	{ KEY_W, Key::W },
	{ KEY_A, Key::A },
	{ KEY_S, Key::S },
	{ KEY_D, Key::D },
	{ KEY_F, Key::F },
	// Gamepad face/shoulder/system buttons for TrimUI TG5040 (Nintendo physical layout).
	// Physical A (right, printed A) = BTN_EAST (305) -> ENTER (Confirm) & Left Click
	// Physical B (bottom, printed B) = BTN_SOUTH (304) -> ESCAPE (Cancel/Back) & Right Click
	// Physical X (top, printed X) = BTN_WEST (308) -> SPACE (Pickup / Action)
	// Physical Y (left, printed Y) = BTN_NORTH (307) -> TAB (Big Map / Focus)
	{ BTN_EAST, Key::ENTER },
	{ BTN_SOUTH, Key::ESCAPE },
	{ BTN_WEST, Key::SPACE },
	{ BTN_NORTH, Key::TAB },
	{ BTN_TL, Key::Q },
	{ BTN_TR, Key::E },
	{ BTN_TL2, Key(0x31) },
	{ BTN_TR2, Key(0x32) },
	{ BTN_START, Key::F10 },
	{ BTN_SELECT, Key::ALT },
	{ BTN_MODE, Key::F11 },
	{ BTN_THUMBL, Key::SHIFT },
	{ BTN_THUMBR, Key::CTRL },
	{ BTN_DPAD_UP, Key::UP },
	{ BTN_DPAD_DOWN, Key::DOWN },
	{ BTN_DPAD_LEFT, Key::LEFT },
	{ BTN_DPAD_RIGHT, Key::RIGHT },
};
const int DisplayServerFbdev::key_map_size = sizeof(DisplayServerFbdev::key_map) / sizeof(DisplayServerFbdev::key_map[0]);

Vector<String> DisplayServerFbdev::get_rendering_drivers_func() {
	Vector<String> drivers;
	drivers.push_back("opengl3_es");
	drivers.push_back("opengl3");
	return drivers;
}

bool DisplayServerFbdev::has_feature(Feature p_feature) const {
	switch (p_feature) {
		case FEATURE_GLOBAL_MENU:
		case FEATURE_SUBWINDOWS:
		case FEATURE_TOUCHSCREEN:
		case FEATURE_MOUSE_WARP:
		case FEATURE_CLIPBOARD:
		case FEATURE_VIRTUAL_KEYBOARD:
		case FEATURE_CURSOR_SHAPE:
		case FEATURE_SCREEN_CAPTURE:
		case FEATURE_NATIVE_DIALOG:
		case FEATURE_IME:
		case FEATURE_ICON:
		case FEATURE_NATIVE_ICON:
		case FEATURE_STATUS_INDICATOR:
		case FEATURE_ORIENTATION:
		case FEATURE_TEXT_TO_SPEECH:
		case FEATURE_EXTEND_TO_TITLE:
		case FEATURE_SCREEN_EXCLUDE_FROM_CAPTURE:
		case FEATURE_WINDOW_TRANSPARENCY:
		case FEATURE_HIDPI:
		case FEATURE_CLIPBOARD_PRIMARY:
		case FEATURE_WINDOW_EMBEDDING:
			return false;
		default:
			return false;
	}
}

Vector<DisplayServer::WindowID> DisplayServerFbdev::get_window_list() const {
	Vector<WindowID> list;
	list.push_back(MAIN_WINDOW_ID);
	return list;
}

void DisplayServerFbdev::window_set_vsync_mode(VSyncMode p_vsync_mode, WindowID p_window) {
	if (gl_manager) {
		gl_manager->set_use_vsync(p_vsync_mode != VSYNC_DISABLED);
	}
}

void DisplayServerFbdev::release_rendering_thread() {
	if (gl_manager) {
		gl_manager->release_current();
	}
}

void DisplayServerFbdev::gl_manager_make_current() {
	if (gl_manager) {
		gl_manager->make_current();
	}
}

void DisplayServerFbdev::swap_buffers() {
	if (gl_manager) {
		gl_manager->swap_buffers();
	}
}

void DisplayServerFbdev::_dispatch_input(const Ref<InputEvent> &p_event) {
	if (input_event_callback.is_valid()) {
		input_event_callback.call(p_event);
	}
}

void DisplayServerFbdev::_emit_key(Key p_key, bool p_pressed) {
	Ref<InputEventKey> ev;
	ev.instantiate();
	ev->set_keycode(p_key);
	ev->set_physical_keycode(p_key);
	ev->set_pressed(p_pressed);
	_dispatch_input(ev);
}

void DisplayServerFbdev::_emit_mouse_button(MouseButton p_button, bool p_pressed, const Point2i &p_position) {
	Ref<InputEventMouseButton> ev;
	ev.instantiate();
	ev->set_button_index(p_button);
	ev->set_position(p_position);
	ev->set_global_position(p_position);
	ev->set_pressed(p_pressed);
	_dispatch_input(ev);
}

void DisplayServerFbdev::_emit_mouse_motion(const Point2i &p_position) {
	const Point2i relative = p_position - mouse_position;
	mouse_position = p_position;
	Ref<InputEventMouseMotion> ev;
	ev.instantiate();
	ev->set_position(p_position);
	ev->set_global_position(p_position);
	ev->set_relative(relative);
	_dispatch_input(ev);
}

void DisplayServerFbdev::_open_input_devices() {
	// Explicit nodes win, then every event node that reports keys or absolute axes.
	const char *env = getenv("GODOT_FBDEV_INPUT");
	if (env != nullptr && env[0] != '\0') {
		Vector<String> nodes = String(env).split(",");
		for (const String &node : nodes) {
			int fd = open(node.strip_edges().utf8().get_data(), O_RDONLY | O_NONBLOCK | O_CLOEXEC);
			if (fd >= 0) {
				input_fds.push_back(fd);
			} else {
				WARN_PRINT(vformat("fbdev: cannot open input node %s.", node));
			}
		}
	} else {
		for (int i = 0; i < 32; i++) {
			String path = vformat("/dev/input/event%d", i);
			int fd = open(path.utf8().get_data(), O_RDONLY | O_NONBLOCK | O_CLOEXEC);
			if (fd < 0) {
				continue;
			}
			unsigned long key_bits[(KEY_MAX / (8 * sizeof(unsigned long))) + 1];
			memset(key_bits, 0, sizeof(key_bits));
			if (ioctl(fd, EVIOCGBIT(EV_KEY, sizeof(key_bits)), key_bits) < 0) {
				close(fd);
				continue;
			}
			const bool has_keys = (key_bits[KEY_UP / (8 * sizeof(unsigned long))] & (1UL << (KEY_UP % (8 * sizeof(unsigned long))))) != 0 ||
					(key_bits[BTN_A / (8 * sizeof(unsigned long))] & (1UL << (BTN_A % (8 * sizeof(unsigned long))))) != 0;
			if (!has_keys) {
				close(fd);
				continue;
			}
			input_fds.push_back(fd);
			print_verbose(vformat("fbdev: listening on %s.", path));
		}
	}
	print_line(vformat("fbdev: %d input device(s).", input_fds.size()));
	mouse_position = Point2i(window_size.width / 2, window_size.height / 2);
}

void DisplayServerFbdev::_close_input_devices() {
	for (int fd : input_fds) {
		close(fd);
	}
	input_fds.clear();
}

void DisplayServerFbdev::_poll_input() {
	if (input_fds.is_empty()) {
		return;
	}
	struct pollfd fds[32];
	const int count = MIN((int)input_fds.size(), 32);
	for (int i = 0; i < count; i++) {
		fds[i].fd = input_fds[i];
		fds[i].events = POLLIN;
		fds[i].revents = 0;
	}
	const int ready = poll(fds, count, 0);
	if (ready <= 0) {
		return;
	}
	for (int i = 0; i < count; i++) {
		if ((fds[i].revents & POLLIN) == 0) {
			continue;
		}
		struct input_event events[32];
		const ssize_t bytes = read(fds[i].fd, events, sizeof(events));
		if (bytes <= 0) {
			continue;
		}
		const int total = (int)(bytes / (ssize_t)sizeof(struct input_event));
		for (int e = 0; e < total; e++) {
			const struct input_event &ev = events[e];
			if (ev.type == EV_KEY) {
				bool handled = false;
				for (int k = 0; k < key_map_size; k++) {
					if (key_map[k].code != (int)ev.code) {
						continue;
					}
					_emit_key(key_map[k].key, ev.value != 0);
					handled = true;
					break;
				}
				// Physical A (BTN_EAST / 305): Left click (Confirm / Select / Attack)
				if ((int)ev.code == BTN_EAST) {
					const bool pressed = ev.value != 0;
					if (mouse_button_pressed[0] != pressed) {
						mouse_button_pressed[0] = pressed;
						_emit_mouse_button(MouseButton::LEFT, pressed, mouse_position);
					}
				}
				// Physical B (BTN_SOUTH / 304): Right click (Cancel / Run / Move)
				if ((int)ev.code == BTN_SOUTH) {
					const bool pressed = ev.value != 0;
					if (mouse_button_pressed[1] != pressed) {
						mouse_button_pressed[1] = pressed;
						_emit_mouse_button(MouseButton::RIGHT, pressed, mouse_position);
					}
				}
				(void)handled;
			} else if (ev.type == EV_ABS) {
				if (ev.code == ABS_X || ev.code == ABS_RX) {
					stick_x = (int)ev.value;
					if (stick_x > 16000) {
						if (stick_dir_x != 1) {
							if (stick_dir_x == -1) _emit_key(Key::LEFT, false);
							_emit_key(Key::RIGHT, true);
							stick_dir_x = 1;
						}
					} else if (stick_x < -16000) {
						if (stick_dir_x != -1) {
							if (stick_dir_x == 1) _emit_key(Key::RIGHT, false);
							_emit_key(Key::LEFT, true);
							stick_dir_x = -1;
						}
					} else if (abs(stick_x) < 10000) {
						if (stick_dir_x == 1) _emit_key(Key::RIGHT, false);
						if (stick_dir_x == -1) _emit_key(Key::LEFT, false);
						stick_dir_x = 0;
					}
				} else if (ev.code == ABS_Y || ev.code == ABS_RY) {
					stick_y = (int)ev.value;
					if (stick_y > 16000) {
						if (stick_dir_y != 1) {
							if (stick_dir_y == -1) _emit_key(Key::UP, false);
							_emit_key(Key::DOWN, true);
							stick_dir_y = 1;
						}
					} else if (stick_y < -16000) {
						if (stick_dir_y != -1) {
							if (stick_dir_y == 1) _emit_key(Key::DOWN, false);
							_emit_key(Key::UP, true);
							stick_dir_y = -1;
						}
					} else if (abs(stick_y) < 10000) {
						if (stick_dir_y == 1) _emit_key(Key::DOWN, false);
						if (stick_dir_y == -1) _emit_key(Key::UP, false);
						stick_dir_y = 0;
					}
				} else if (ev.code == ABS_HAT0X) {
					const int val = (int)ev.value;
					if (val != dpad_x) {
						if (dpad_x == -1) _emit_key(Key::LEFT, false);
						if (dpad_x == 1) _emit_key(Key::RIGHT, false);
						if (val == -1) _emit_key(Key::LEFT, true);
						if (val == 1) _emit_key(Key::RIGHT, true);
						dpad_x = val;
					}
				} else if (ev.code == ABS_HAT0Y) {
					const int val = (int)ev.value;
					if (val != dpad_y) {
						if (dpad_y == -1) _emit_key(Key::UP, false);
						if (dpad_y == 1) _emit_key(Key::DOWN, false);
						if (val == -1) _emit_key(Key::UP, true);
						if (val == 1) _emit_key(Key::DOWN, true);
						dpad_y = val;
					}
				}
			}
		}
	}
	// Update virtual mouse cursor smoothly from analog stick (-32767..32767, deadzone 4000)
	if (abs(stick_x) > 4000 || abs(stick_y) > 4000) {
		int move_x = 0;
		int move_y = 0;
		if (abs(stick_x) > 4000) {
			move_x = (stick_x > 0 ? (stick_x - 4000) : (stick_x + 4000)) / 2500;
		}
		if (abs(stick_y) > 4000) {
			move_y = (stick_y > 0 ? (stick_y - 4000) : (stick_y + 4000)) / 2500;
		}
		if (move_x != 0 || move_y != 0) {
			Point2i next = mouse_position + Point2i(move_x, move_y);
			next.x = CLAMP(next.x, 0, window_size.width - 1);
			next.y = CLAMP(next.y, 0, window_size.height - 1);
			_emit_mouse_motion(next);
		}
	}
}

void DisplayServerFbdev::process_events() {
	_poll_input();
	Input::get_singleton()->flush_buffered_events();
}

DisplayServer *DisplayServerFbdev::create_func(const String &p_rendering_driver, WindowMode p_mode, VSyncMode p_vsync_mode, uint32_t p_flags, const Vector2i *p_position, const Vector2i &p_resolution, int p_screen, Context p_context, int64_t p_parent_window, Error &r_error) {
	DisplayServer *ds = memnew(DisplayServerFbdev(p_rendering_driver, p_mode, p_vsync_mode, p_flags, p_position, p_resolution, p_screen, p_context, p_parent_window, r_error));
	return ds;
}

void DisplayServerFbdev::register_fbdev_driver() {
	register_create_function("fbdev", create_func, get_rendering_drivers_func);
}

DisplayServerFbdev::DisplayServerFbdev(const String &p_rendering_driver, WindowMode p_mode, VSyncMode p_vsync_mode, uint32_t p_flags, const Vector2i *p_position, const Vector2i &p_resolution, int p_screen, Context p_context, int64_t p_parent_window, Error &r_error) {
	r_error = OK;

	FBDEV_TRACE("display server ctor");
	gl_manager = memnew(GLManagerFbdev);
	if (gl_manager->initialize(p_resolution.width, p_resolution.height) != OK) {
		memdelete(gl_manager);
		gl_manager = nullptr;
		r_error = ERR_UNAVAILABLE;
		return;
	}
	FBDEV_TRACE("rasterizer make current");
	if (p_rendering_driver == "opengl3") {
		RasterizerGLES3::make_current(true);
	} else {
		RasterizerGLES3::make_current(false);
	}
	gl_manager->set_use_vsync(p_vsync_mode != VSYNC_DISABLED);

	FBDEV_TRACE("read framebuffer info");
	// The window is the render target, not the panel: rendering at a lower
	// internal resolution is the only lever that reduces GPU time on this SoC.
	window_size = Size2i(gl_manager->get_render_width(), gl_manager->get_render_height());
	window_id = MAIN_WINDOW_ID;

	_open_input_devices();

	print_line(vformat("fbdev: display server ready, %dx%d.", window_size.width, window_size.height));
}

DisplayServerFbdev::~DisplayServerFbdev() {
	_close_input_devices();
	if (gl_manager) {
		memdelete(gl_manager);
		gl_manager = nullptr;
	}
}

#endif // FBDEV_ENABLED
