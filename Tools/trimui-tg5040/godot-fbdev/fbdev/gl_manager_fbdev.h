/**************************************************************************/
/*  gl_manager_fbdev.h                                                    */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             GODOT ENGINE                               */
/**************************************************************************/

#pragma once

#ifdef FBDEV_ENABLED

#include "core/os/mutex.h"
#include "core/templates/vector.h"
#include "platform_gl.h"
#include "servers/display/display_server.h"

/**
 * Offscreen EGL rendering for devices whose only display path is /dev/fb0.
 *
 * The vendor EGL on such SoCs (e.g. Imagination PowerVR on Allwinner A133)
 * rejects the legacy fbdev_window native window (EGL_BAD_NATIVE_WINDOW), so the
 * context is bound to a pbuffer instead: the scene renders offscreen and every
 * frame is converted to the framebuffer's pixel format and written into a page
 * of /dev/fb0, which is then flipped with FBIOPAN_DISPLAY.
 */
class GLManagerFbdev {
public:
	struct FramebufferInfo {
		int width = 0;
		int height = 0;
		int stride = 0; // bytes per line
		int bpp = 32;
		uint32_t r_offset = 16;
		uint32_t g_offset = 8;
		uint32_t b_offset = 0;
		uint32_t a_offset = 24;
		uint32_t r_length = 8;
		uint32_t g_length = 8;
		uint32_t b_length = 8;
		uint32_t a_length = 8;
		bool panning = false; // FBIOBLANK/FBIOPAN available
	};

private:
	// /dev/fb0
	int fb_fd = -1;
	uint8_t *fb_mem = nullptr;
	size_t fb_size = 0;
	FramebufferInfo fb_info;
	int active_page = 0;
	int page_count = 1;

	// EGL
	EGLDisplay egl_display = EGL_NO_DISPLAY;
	EGLConfig egl_config = nullptr;
	EGLContext egl_context = EGL_NO_CONTEXT;
	EGLSurface egl_surface = EGL_NO_SURFACE;

	// Readback staging (RGBA8) and converted page.
	Vector<uint8_t> readback;
	Vector<uint8_t> converted;

	bool vsync_enabled = true;
	int render_width = 0;
	int render_height = 0;
	int render_scale = 1;

	// Timing (printed once per second when GODOT_FBDEV_DEBUG=1).
	uint64_t stat_frames = 0;
	uint64_t stat_read_us = 0;
	uint64_t stat_convert_us = 0;
	uint64_t stat_blit_us = 0;
	uint64_t stat_last_report_us = 0;

	Error _open_framebuffer();
	void _close_framebuffer();
	Error _create_context();
	void _destroy_context();
	bool _select_config(EGLConfig *r_config);

	static String _egl_error_string(EGLint p_error);

public:
	static bool is_available();

	Error initialize(int p_width, int p_height);
	void shutdown();

	bool is_initialized() const { return egl_context != EGL_NO_CONTEXT && fb_mem != nullptr; }

	void make_current();
	void release_current();
	/** Render, read back and present one frame to /dev/fb0. */
	void swap_buffers();

	void set_use_vsync(bool p_use) { vsync_enabled = p_use; }
	bool is_using_vsync() const { return vsync_enabled; }

	const FramebufferInfo &get_framebuffer_info() const { return fb_info; }
	int get_render_width() const { return render_width; }
	int get_render_height() const { return render_height; }

	GLManagerFbdev() = default;
	~GLManagerFbdev();
};

#endif // FBDEV_ENABLED
