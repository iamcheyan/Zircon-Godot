/**************************************************************************/
/*  display_server_fbdev.h                                                */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             GODOT ENGINE                               */
/**************************************************************************/

#pragma once

#ifdef FBDEV_ENABLED

#include "servers/display/display_server.h"

class GLManagerFbdev;

/**
 * Display server for Linux devices that only expose a framebuffer.
 *
 * Typical target: handhelds whose vendor kernel provides /dev/fb0 plus an EGL
 * driver without any window system (no X11, no Wayland, no KMS connector).
 * There is exactly one fullscreen window; rendering goes through
 * GLManagerFbdev, input is read directly from evdev nodes.
 */
class DisplayServerFbdev : public DisplayServer {
	GDSOFTCLASS(DisplayServerFbdev, DisplayServer);

	GLManagerFbdev *gl_manager = nullptr;

	WindowID window_id = MAIN_WINDOW_ID;
	Size2i window_size;
	Callable input_event_callback;

	// Input state.
	Vector<int> input_fds;
	bool handheld_input = false;
	bool pointer_input = false;
	bool confirm_pressed = false;
	bool confirm_pointer = false;
	uint64_t last_click_usec = 0;
	Point2i last_click_position;
	bool left_trigger_pressed = false;
	bool right_trigger_pressed = false;
	// Linux input_absinfo {value, minimum, maximum, fuzz, flat, resolution}.
	int axis_min[6] = { -32768, -32768, 0, -32768, -32768, 0 };
	int axis_max[6] = { 32767, 32767, 255, 32767, 32767, 255 };
	Point2i mouse_position;
	bool mouse_button_pressed[8] = { false };
	int left_stick_x = 0;
	int left_stick_y = 0;
	float right_stick_x = 0.0f;
	float right_stick_y = 0.0f;
	int dpad_x = 0;
	int dpad_y = 0;

	struct KeyMap {
		int code;
		Key key;
	};
	static const KeyMap key_map[];
	static const int key_map_size;

	void _open_input_devices();
	void _close_input_devices();
	void _poll_input();
	void _dispatch_input(const Ref<InputEvent> &p_event);
	static void _dispatch_input_events(const Ref<InputEvent> &p_event);
	void _emit_key(Key p_key, bool p_pressed, bool p_echo = false);
	float _normalize_axis(int p_code, int p_value) const;
	void _emit_mouse_button(MouseButton p_button, bool p_pressed, const Point2i &p_position);
	void _emit_mouse_motion(const Point2i &p_position);

	static Vector<String> get_rendering_drivers_func();
	static DisplayServer *create_func(const String &p_rendering_driver, WindowMode p_mode, VSyncMode p_vsync_mode, uint32_t p_flags, const Vector2i *p_position, const Vector2i &p_resolution, int p_screen, Context p_context, int64_t p_parent_window, Error &r_error);

public:
	static void register_fbdev_driver();

	String get_name() const override { return "fbdev"; }
	bool has_feature(Feature p_feature) const override;

	int get_screen_count() const override { return 1; }
	int get_primary_screen() const override { return 0; }
	Point2i screen_get_position(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return Point2i(); }
	Size2i screen_get_size(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return window_size; }
	Rect2i screen_get_usable_rect(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return Rect2i(Point2i(), window_size); }
	int screen_get_dpi(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return 160; }
	float screen_get_scale(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return 1; }
	float screen_get_max_scale() const override { return 1; }
	float screen_get_refresh_rate(int p_screen = SCREEN_OF_MAIN_WINDOW) const override { return 60.0f; }
	void screen_set_orientation(ScreenOrientation p_orientation, int p_screen = SCREEN_OF_MAIN_WINDOW) override {}
	void screen_set_keep_on(bool p_enable) override {}

	Vector<WindowID> get_window_list() const override;

	WindowID create_sub_window(WindowMode p_mode, VSyncMode p_vsync_mode, uint32_t p_flags, const Rect2i &p_rect = Rect2i(), bool p_exclusive = false, WindowID p_transient_parent = INVALID_WINDOW_ID) override { return INVALID_WINDOW_ID; }
	void show_window(WindowID p_id) override {}
	void delete_sub_window(WindowID p_id) override {}

	WindowID get_window_at_screen_position(const Point2i &p_position) const override { return MAIN_WINDOW_ID; }

	void window_attach_instance_id(ObjectID p_instance, WindowID p_window = MAIN_WINDOW_ID) override {}
	ObjectID window_get_attached_instance_id(WindowID p_window = MAIN_WINDOW_ID) const override { return ObjectID(); }

	void window_set_rect_changed_callback(const Callable &p_callable, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_window_event_callback(const Callable &p_callable, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_input_event_callback(const Callable &p_callable, WindowID p_window = MAIN_WINDOW_ID) override { input_event_callback = p_callable; }
	void window_set_input_text_callback(const Callable &p_callable, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_drop_files_callback(const Callable &p_callable, WindowID p_window = MAIN_WINDOW_ID) override {}

	void window_set_title(const String &p_title, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_mouse_passthrough(const Vector<Vector2> &p_region, WindowID p_window = MAIN_WINDOW_ID) override {}

	int window_get_current_screen(WindowID p_window = MAIN_WINDOW_ID) const override { return 0; }
	void window_set_current_screen(int p_screen, WindowID p_window = MAIN_WINDOW_ID) override {}

	Point2i window_get_position(WindowID p_window = MAIN_WINDOW_ID) const override { return Point2i(); }
	Point2i window_get_position_with_decorations(WindowID p_window = MAIN_WINDOW_ID) const override { return Point2i(); }
	void window_set_position(const Point2i &p_position, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_transient(WindowID p_window, WindowID p_parent) override {}

	void window_set_max_size(const Size2i p_size, WindowID p_window = MAIN_WINDOW_ID) override {}
	Size2i window_get_max_size(WindowID p_window = MAIN_WINDOW_ID) const override { return window_size; }
	void window_set_min_size(const Size2i p_size, WindowID p_window = MAIN_WINDOW_ID) override {}
	Size2i window_get_min_size(WindowID p_window = MAIN_WINDOW_ID) const override { return Size2i(); }

	void window_set_size(const Size2i p_size, WindowID p_window = MAIN_WINDOW_ID) override {}
	Size2i window_get_size(WindowID p_window = MAIN_WINDOW_ID) const override { return window_size; }
	Size2i window_get_size_with_decorations(WindowID p_window = MAIN_WINDOW_ID) const override { return window_size; }

	void window_set_mode(WindowMode p_mode, WindowID p_window = MAIN_WINDOW_ID) override {}
	WindowMode window_get_mode(WindowID p_window = MAIN_WINDOW_ID) const override { return WINDOW_MODE_FULLSCREEN; }

	void window_set_vsync_mode(VSyncMode p_vsync_mode, WindowID p_window = MAIN_WINDOW_ID) override;
	VSyncMode window_get_vsync_mode(WindowID p_window) const override { return VSyncMode::VSYNC_ENABLED; }

	bool window_is_maximize_allowed(WindowID p_window = MAIN_WINDOW_ID) const override { return false; }
	void window_set_flag(WindowFlags p_flag, bool p_enabled, WindowID p_window = MAIN_WINDOW_ID) override {}
	bool window_get_flag(WindowFlags p_flag, WindowID p_window = MAIN_WINDOW_ID) const override { return false; }

	void window_request_attention(WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_move_to_foreground(WindowID p_window = MAIN_WINDOW_ID) override {}
	bool window_is_focused(WindowID p_window = MAIN_WINDOW_ID) const override { return true; }

	bool window_can_draw(WindowID p_window = MAIN_WINDOW_ID) const override { return true; }
	bool can_any_window_draw() const override { return true; }

	void window_set_ime_active(const bool p_active, WindowID p_window = MAIN_WINDOW_ID) override {}
	void window_set_ime_position(const Point2i &p_pos, WindowID p_window = MAIN_WINDOW_ID) override {}

	int64_t window_get_native_handle(HandleType p_handle_type, WindowID p_window = MAIN_WINDOW_ID) const override { return 0; }

	void process_events() override;

	void set_native_icon(const String &p_filename) override {}
	void set_icon(const Ref<Image> &p_icon) override {}

	void help_set_search_callbacks(const Callable &p_search_callback = Callable(), const Callable &p_action_callback = Callable()) override {}

	bool tts_is_speaking() const override { return false; }
	bool tts_is_paused() const override { return false; }
	TypedArray<Dictionary> tts_get_voices() const override { return TypedArray<Dictionary>(); }
	void tts_speak(const String &p_text, const String &p_voice, int p_volume = 50, float p_pitch = 1.0f, float p_rate = 1.0f, int64_t p_utterance_id = 0, bool p_interrupt = false) override {}
	void tts_pause() override {}
	void tts_resume() override {}
	void tts_stop() override {}

	void mouse_set_mode(MouseMode p_mode) override {}
	void mouse_set_mode_override(MouseMode p_mode) override {}
	void mouse_set_mode_override_enabled(bool p_override_enabled) override {}
	Point2i mouse_get_position() const override { return mouse_position; }
	void clipboard_set(const String &p_text) override {}
	void clipboard_set_primary(const String &p_text) override {}

	void virtual_keyboard_show(const String &p_existing_text, const Rect2 &p_screen_rect = Rect2(), VirtualKeyboardType p_type = KEYBOARD_TYPE_DEFAULT, int p_max_length = -1, int p_cursor_start = -1, int p_cursor_end = -1) override {}
	void virtual_keyboard_hide() override {}

	void cursor_set_shape(CursorShape p_shape) override {}
	void cursor_set_custom_image(const Ref<Resource> &p_cursor, CursorShape p_shape = CURSOR_ARROW, const Vector2 &p_hotspot = Vector2()) override {}

	Error dialog_show(String p_title, String p_description, Vector<String> p_buttons, const Callable &p_callback) override { return ERR_UNAVAILABLE; }
	Error dialog_input_text(String p_title, String p_description, String p_partial, const Callable &p_callback) override { return ERR_UNAVAILABLE; }
	Error file_dialog_show(const String &p_title, const String &p_current_directory, const String &p_filename, bool p_show_hidden, FileDialogMode p_mode, const Vector<String> &p_filters, const Callable &p_callback, WindowID p_window_id) override { return ERR_UNAVAILABLE; }
	Error file_dialog_with_options_show(const String &p_title, const String &p_current_directory, const String &p_root, const String &p_filename, bool p_show_hidden, FileDialogMode p_mode, const Vector<String> &p_filters, const TypedArray<Dictionary> &p_options, const Callable &p_callback, WindowID p_window_id) override { return ERR_UNAVAILABLE; }

	void release_rendering_thread() override;
	void swap_buffers() override;
	void window_make_current() { gl_manager_make_current(); }
	void gl_manager_make_current();

	IndicatorID create_status_indicator(const Ref<Texture2D> &p_icon, const String &p_tooltip, const Callable &p_callback) override { return INVALID_INDICATOR_ID; }
	void status_indicator_set_icon(IndicatorID p_id, const Ref<Texture2D> &p_icon) override {}
	void status_indicator_set_tooltip(IndicatorID p_id, const String &p_tooltip) override {}
	void status_indicator_set_callback(IndicatorID p_id, const Callable &p_callback) override {}
	void delete_status_indicator(IndicatorID p_id) override {}

	DisplayServerFbdev(const String &p_rendering_driver, WindowMode p_mode, VSyncMode p_vsync_mode, uint32_t p_flags, const Vector2i *p_position, const Vector2i &p_resolution, int p_screen, Context p_context, int64_t p_parent_window, Error &r_error);
	~DisplayServerFbdev();
};

#endif // FBDEV_ENABLED
