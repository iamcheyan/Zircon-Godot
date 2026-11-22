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

#include <cmath>
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
	// A/B are Enter/Escape in semantic mode; pointer fallback sends A as click
	// ONLY. Non-handheld users retain the legacy Enter+click / Escape+click.
	// Physical X (top, printed X) = BTN_WEST (308) -> SPACE (Pickup)
	// Physical Y (left, printed Y) = BTN_NORTH (307) -> F16 (Normal attack)
	{ BTN_EAST, Key::ENTER },
	{ BTN_SOUTH, Key::ESCAPE },
	{ BTN_WEST, Key::SPACE },
	{ BTN_NORTH, Key::F16 },
	{ BTN_TL, Key::Q },
	{ BTN_TR, Key::E },
	{ BTN_TL2, Key(0x31) },
	{ BTN_TR2, Key(0x32) },
	{ BTN_START, Key::F10 },
	{ BTN_SELECT, Key::F15 },
	{ BTN_MODE, Key::F11 },
	{ BTN_THUMBL, Key::F13 },
	{ BTN_THUMBR, Key::F14 },
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

void DisplayServerFbdev::_emit_key(Key p_key, bool p_pressed, bool p_echo) {
	Ref<InputEventKey> ev;
	ev.instantiate();
	ev->set_keycode(p_key);
	ev->set_physical_keycode(p_key);
	ev->set_pressed(p_pressed);
	ev->set_echo(p_echo);
	_dispatch_input(ev);
}

float DisplayServerFbdev::_normalize_axis(int p_code, int p_value) const {
	if (p_code < 0 || p_code >= 6 || axis_max[p_code] <= axis_min[p_code]) {
		return 0.0f;
	}
	const float fraction = (float)(p_value - axis_min[p_code]) / (float)(axis_max[p_code] - axis_min[p_code]);
	return CLAMP(fraction * 2.0f - 1.0f, -1.0f, 1.0f);
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
	const char *handheld = getenv("ZIRCON_HANDHELD");
	handheld_input = handheld != nullptr && strcmp(handheld, "1") == 0;
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
	if (handheld_input) {
		for (int fd : input_fds) {
			for (int axis = ABS_X; axis <= ABS_RZ; axis++) {
				struct input_absinfo info;
				if (ioctl(fd, EVIOCGABS(axis), &info) == 0 && info.maximum > info.minimum) {
					axis_min[axis] = info.minimum;
					axis_max[axis] = info.maximum;
				}
			}
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
	// The game changes this at modal boundaries. Legacy dialogs/default HUD keep
	// a pointer fallback; the compact HUD/quick panels receive semantic keys only.
	const char *pointer_mode = getenv("ZIRCON_HANDHELD_POINTER");
	pointer_input = handheld_input && pointer_mode != nullptr && strcmp(pointer_mode, "1") == 0;
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
	if (ready < 0 || (ready == 0 && !handheld_input)) {
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
				// Use the mode at press time for the matching release, even if the
				// press changes the current screen or opens a dialog.
				if (handheld_input && ev.code == BTN_EAST) {
					const bool pressed = ev.value != 0;
					if (pressed != confirm_pressed) {
						confirm_pressed = pressed;
						if (pressed) {
							confirm_pointer = pointer_input;
						}
						if (confirm_pointer) {
							_emit_mouse_button(MouseButton::LEFT, pressed, mouse_position);
						} else {
							_emit_key(Key::ENTER, pressed);
						}
					}
					continue;
				}
				for (int k = 0; k < key_map_size; k++) {
					if (key_map[k].code != (int)ev.code) {
						continue;
					}
					Key mapped = key_map[k].key;
					if (!handheld_input) {
						if (ev.code == BTN_NORTH) mapped = Key::TAB;
						if (ev.code == BTN_SELECT) mapped = Key::ALT;
						if (ev.code == BTN_THUMBL) mapped = Key::SHIFT;
						if (ev.code == BTN_THUMBR) mapped = Key::CTRL;
					}
					_emit_key(mapped, ev.value != 0, handheld_input && ev.value == 2);
					break;
				}
				// In Zircon handheld mode face buttons are semantic keyboard actions;
				// do not also synthesize mouse clicks, otherwise A/B execute two paths.
				if (handheld_input) {
					continue;
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
			} else if (ev.type == EV_ABS) {
				if (ev.code == ABS_X) {
					left_stick_x = ev.value;
					Input::get_singleton()->joy_axis(0, JoyAxis::LEFT_X, handheld_input ? _normalize_axis(ABS_X, ev.value) : ev.value / 32767.0f);
				} else if (ev.code == ABS_Y) {
					left_stick_y = ev.value;
					Input::get_singleton()->joy_axis(0, JoyAxis::LEFT_Y, handheld_input ? _normalize_axis(ABS_Y, ev.value) : ev.value / 32767.0f);
				} else if (ev.code == ABS_RX) {
					right_stick_x = handheld_input ? _normalize_axis(ABS_RX, ev.value) : ev.value / 32767.0f;
					Input::get_singleton()->joy_axis(0, JoyAxis::RIGHT_X, right_stick_x);
				} else if (ev.code == ABS_RY) {
					right_stick_y = handheld_input ? _normalize_axis(ABS_RY, ev.value) : ev.value / 32767.0f;
					Input::get_singleton()->joy_axis(0, JoyAxis::RIGHT_Y, right_stick_y);
				} else if (handheld_input && (ev.code == ABS_Z || ev.code == ABS_RZ)) {
					const int axis = ev.code;
					const float fraction = (float)(ev.value - axis_min[axis]) / MAX(1, axis_max[axis] - axis_min[axis]);
					bool &down = axis == ABS_Z ? left_trigger_pressed : right_trigger_pressed;
					const bool pressed = fraction > (down ? 0.35f : 0.60f);
					if (pressed != down) {
						down = pressed;
						// linux/input.h defines KEY_1/KEY_2 as evdev macros, not Godot keycodes.
						_emit_key(Key(axis == ABS_Z ? '1' : '2'), pressed);
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
	// Desktop/fallback mode uses the left stick as a virtual mouse. Zircon's
	// handheld layer uses both sticks semantically and must not receive cursor
	// motion or CombatController mouse hover events from the same stick.
	if (handheld_input && !pointer_input) {
		return;
	}
	if (pointer_input) {
		const float x = right_stick_x;
		const float y = right_stick_y;
		if (std::abs(x) > 0.25f || std::abs(y) > 0.25f) {
			Point2i next = mouse_position + Point2i((int)(x * 10), (int)(y * 10));
			next.x = CLAMP(next.x, 0, window_size.width - 1);
			next.y = CLAMP(next.y, 0, window_size.height - 1);
			_emit_mouse_motion(next);
		}
		return;
	}
	// Update virtual mouse cursor smoothly from Left Stick (-32767..32767, deadzone 4000)
	if (abs(left_stick_x) > 4000 || abs(left_stick_y) > 4000) {
		int move_x = 0;
		int move_y = 0;
		if (abs(left_stick_x) > 4000) {
			move_x = (left_stick_x > 0 ? (left_stick_x - 4000) : (left_stick_x + 4000)) / 2500;
		}
		if (abs(left_stick_y) > 4000) {
			move_y = (left_stick_y > 0 ? (left_stick_y - 4000) : (left_stick_y + 4000)) / 2500;
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
	Input::get_singleton()->joy_connection_changed(0, true, "TRIMUI Player1", "TG5040");

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
