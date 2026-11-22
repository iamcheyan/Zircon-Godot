/**************************************************************************/
/*  gl_manager_fbdev.cpp                                                  */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             GODOT ENGINE                               */
/**************************************************************************/

#include "gl_manager_fbdev.h"

#ifdef FBDEV_ENABLED

#include "core/os/os.h"

#include <fcntl.h>
#include <linux/fb.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <unistd.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

String GLManagerFbdev::_egl_error_string(EGLint p_error) {
	switch (p_error) {
		case EGL_SUCCESS:
			return "EGL_SUCCESS";
		case EGL_NOT_INITIALIZED:
			return "EGL_NOT_INITIALIZED";
		case EGL_BAD_ACCESS:
			return "EGL_BAD_ACCESS";
		case EGL_BAD_ALLOC:
			return "EGL_BAD_ALLOC";
		case EGL_BAD_ATTRIBUTE:
			return "EGL_BAD_ATTRIBUTE";
		case EGL_BAD_CONTEXT:
			return "EGL_BAD_CONTEXT";
		case EGL_BAD_CONFIG:
			return "EGL_BAD_CONFIG";
		case EGL_BAD_CURRENT_SURFACE:
			return "EGL_BAD_CURRENT_SURFACE";
		case EGL_BAD_DISPLAY:
			return "EGL_BAD_DISPLAY";
		case EGL_BAD_SURFACE:
			return "EGL_BAD_SURFACE";
		case EGL_BAD_MATCH:
			return "EGL_BAD_MATCH";
		case EGL_BAD_PARAMETER:
			return "EGL_BAD_PARAMETER";
		case EGL_BAD_NATIVE_PIXMAP:
			return "EGL_BAD_NATIVE_PIXMAP";
		case EGL_BAD_NATIVE_WINDOW:
			return "EGL_BAD_NATIVE_WINDOW";
		case EGL_CONTEXT_LOST:
			return "EGL_CONTEXT_LOST";
		default:
			return itos(p_error);
	}
}

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

Error GLManagerFbdev::_open_framebuffer() {
	FBDEV_TRACE("open framebuffer");
	const char *node = getenv("GODOT_FBDEV");
	if (node == nullptr || node[0] == '\0') {
		node = "/dev/fb0";
	}
	fb_fd = open(node, O_RDWR | O_CLOEXEC);
	if (fb_fd < 0) {
		ERR_FAIL_V_MSG(ERR_CANT_OPEN, vformat("fbdev: cannot open %s (errno %d).", node, errno));
	}

	struct fb_fix_screeninfo fix;
	struct fb_var_screeninfo var;
	if (ioctl(fb_fd, FBIOGET_FSCREENINFO, &fix) < 0) {
		ERR_PRINT("fbdev: FBIOGET_FSCREENINFO failed.");
		return ERR_CANT_OPEN;
	}
	if (ioctl(fb_fd, FBIOGET_VSCREENINFO, &var) < 0) {
		ERR_PRINT("fbdev: FBIOGET_VSCREENINFO failed.");
		return ERR_CANT_OPEN;
	}

	fb_info.width = (int)var.xres;
	fb_info.height = (int)var.yres;
	fb_info.stride = (int)fix.line_length;
	fb_info.bpp = (int)var.bits_per_pixel;
	fb_info.r_offset = var.red.offset;
	fb_info.r_length = var.red.length;
	fb_info.g_offset = var.green.offset;
	fb_info.g_length = var.green.length;
	fb_info.b_offset = var.blue.offset;
	fb_info.b_length = var.blue.length;
	fb_info.a_offset = var.transp.offset;
	fb_info.a_length = var.transp.length;

	// Double buffering when the driver reports room for more than one page.
	page_count = (int)(var.yres_virtual / (var.yres > 0 ? var.yres : 1));
	if (page_count < 1) {
		page_count = 1;
	}
	if (page_count > 3) {
		page_count = 3;
	}
	fb_info.panning = page_count > 1 && (fix.visual != FB_VISUAL_PSEUDOCOLOR);

	fb_size = (size_t)fix.line_length * (size_t)var.yres_virtual;
	if (fb_size == 0) {
		fb_size = (size_t)fix.line_length * (size_t)var.yres;
	}
	fb_mem = (uint8_t *)mmap(nullptr, fb_size, PROT_READ | PROT_WRITE, MAP_SHARED, fb_fd, 0);
	if (fb_mem == MAP_FAILED) {
		fb_mem = nullptr;
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, "fbdev: mmap of the framebuffer failed.");
	}

	print_line(vformat("fbdev: %dx%d, %d bpp, stride %d, %d page(s), panning %s.",
			fb_info.width, fb_info.height, fb_info.bpp, fb_info.stride, page_count,
			fb_info.panning ? "on" : "off"));
	return OK;
}

void GLManagerFbdev::_close_framebuffer() {
	if (fb_mem != nullptr) {
		munmap(fb_mem, fb_size);
		fb_mem = nullptr;
	}
	if (fb_fd >= 0) {
		close(fb_fd);
		fb_fd = -1;
	}
}

bool GLManagerFbdev::_select_config(EGLConfig *r_config) {
	const EGLint config_attribs[] = {
		EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
		EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT,
		EGL_RED_SIZE, 8,
		EGL_GREEN_SIZE, 8,
		EGL_BLUE_SIZE, 8,
		EGL_ALPHA_SIZE, 8,
		EGL_DEPTH_SIZE, 24,
		EGL_STENCIL_SIZE, 8,
		EGL_NONE
	};
	EGLint count = 0;
	if (!eglChooseConfig(egl_display, config_attribs, r_config, 1, &count) || count < 1) {
		ERR_FAIL_V_MSG(false, vformat("fbdev: no EGL config with a pbuffer surface (%s).", _egl_error_string(eglGetError())));
	}
	return true;
}

Error GLManagerFbdev::_create_context() {
	if (!gladLoaderLoadEGL(EGL_NO_DISPLAY)) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, "fbdev: cannot load libEGL.");
	}
	FBDEV_TRACE("eglGetDisplay");
	egl_display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
	if (egl_display == EGL_NO_DISPLAY) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, "fbdev: eglGetDisplay returned no display.");
	}
	// Loading with EGL_NO_DISPLAY only resolves the minimal core; the
	// display-dependent and extension entry points need a real display.
	FBDEV_TRACE("gladLoaderLoadEGL(display)");
	if (!gladLoaderLoadEGL(egl_display)) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, "fbdev: cannot load EGL for the default display.");
	}
	EGLint major = 0;
	EGLint minor = 0;
	FBDEV_TRACE("eglInitialize");
	if (!eglInitialize(egl_display, &major, &minor)) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, vformat("fbdev: eglInitialize failed (%s).", _egl_error_string(eglGetError())));
	}
	if (glad_eglQueryString) {
		const char *vendor = eglQueryString(egl_display, EGL_VENDOR);
		print_line(vformat("fbdev: EGL %d.%d (%s).", major, minor, vendor ? vendor : "unknown"));
	}

	FBDEV_TRACE("eglBindAPI");
	if (fbdev_debug_enabled()) {
		char ptrs[512];
		snprintf(ptrs, sizeof(ptrs),
				"eglBindAPI=%p eglChooseConfig=%p eglCreatePbufferSurface=%p eglCreateContext=%p eglMakeCurrent=%p eglSwapBuffers=%p eglGetError=%p",
				(void *)(glad_eglBindAPI), (void *)(glad_eglChooseConfig), (void *)(glad_eglCreatePbufferSurface),
				(void *)(glad_eglCreateContext), (void *)(glad_eglMakeCurrent), (void *)(glad_eglSwapBuffers),
				(void *)(glad_eglGetError));
		fprintf(stderr, "fbdev-trace: %s\n", ptrs);
		fflush(stderr);
	}
	if (glad_eglBindAPI && !eglBindAPI(EGL_OPENGL_ES_API)) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, "fbdev: eglBindAPI(EGL_OPENGL_ES_API) failed.");
	}
	FBDEV_TRACE("choose config");
	if (!_select_config(&egl_config)) {
		return ERR_CANT_CREATE;
	}

	const EGLint pbuffer_attribs[] = {
		EGL_WIDTH, render_width,
		EGL_HEIGHT, render_height,
		EGL_NONE
	};
	FBDEV_TRACE("create pbuffer");
	egl_surface = eglCreatePbufferSurface(egl_display, egl_config, pbuffer_attribs);
	if (egl_surface == EGL_NO_SURFACE) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, vformat("fbdev: eglCreatePbufferSurface failed (%s).", _egl_error_string(eglGetError())));
	}

	// ES 3 first; fall back to ES 2 for drivers that only expose the older context.
	FBDEV_TRACE("create context");
	const EGLint context_attribs_es3[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
	egl_context = eglCreateContext(egl_display, egl_config, EGL_NO_CONTEXT, context_attribs_es3);
	if (egl_context == EGL_NO_CONTEXT) {
		const EGLint context_attribs_es2[] = { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE };
		egl_context = eglCreateContext(egl_display, egl_config, EGL_NO_CONTEXT, context_attribs_es2);
		print_verbose("fbdev: falling back to an OpenGL ES 2 context.");
	}
	if (egl_context == EGL_NO_CONTEXT) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, vformat("fbdev: eglCreateContext failed (%s).", _egl_error_string(eglGetError())));
	}

	FBDEV_TRACE("make current");
	if (!eglMakeCurrent(egl_display, egl_surface, egl_surface, egl_context)) {
		ERR_FAIL_V_MSG(ERR_CANT_CREATE, vformat("fbdev: eglMakeCurrent failed (%s).", _egl_error_string(eglGetError())));
	}
	return OK;
}

void GLManagerFbdev::_destroy_context() {
	if (egl_display != EGL_NO_DISPLAY) {
		eglMakeCurrent(egl_display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
		if (egl_context != EGL_NO_CONTEXT) {
			eglDestroyContext(egl_display, egl_context);
			egl_context = EGL_NO_CONTEXT;
		}
		if (egl_surface != EGL_NO_SURFACE) {
			eglDestroySurface(egl_display, egl_surface);
			egl_surface = EGL_NO_SURFACE;
		}
		eglTerminate(egl_display);
		egl_display = EGL_NO_DISPLAY;
	}
}

bool GLManagerFbdev::is_available() {
	const char *node = getenv("GODOT_FBDEV");
	return access((node != nullptr && node[0] != '\0') ? node : "/dev/fb0", R_OK | W_OK) == 0;
}

Error GLManagerFbdev::initialize(int p_width, int p_height) {
	Error err = _open_framebuffer();
	if (err != OK) {
		return err;
	}
	// Default to the panel's native size. A lower internal resolution trades
	// sharpness for a much cheaper glReadPixels (the GPU readback dominates the
	// frame time on this SoC); ZIRCON_FBDEV_RENDER_SCALE=2 renders at half size.
	(void)p_width;
	(void)p_height;
	int scale = 1;
	const char *scale_env = getenv("ZIRCON_FBDEV_RENDER_SCALE");
	if (scale_env != nullptr && scale_env[0] != '\0') {
		scale = atoi(scale_env);
		if (scale < 1) {
			scale = 1;
		}
		if (scale > 4) {
			scale = 4;
		}
	}
	render_scale = scale;
	render_width = fb_info.width / scale;
	render_height = fb_info.height / scale;
	print_line(vformat("fbdev: rendering at %dx%d (scale 1/%d).", render_width, render_height, scale));

	// Staging buffers: RGBA8 readback and the converted page.
	readback.resize(render_width * render_height * 4);

	err = _create_context();
	if (err != OK) {
		_close_framebuffer();
		return err;
	}
	make_current();
	FBDEV_TRACE("gl manager ready");
	return OK;
}

void GLManagerFbdev::shutdown() {
	release_current();
	_destroy_context();
	_close_framebuffer();
}

void GLManagerFbdev::make_current() {
	if (egl_display == EGL_NO_DISPLAY || egl_surface == EGL_NO_SURFACE) {
		return;
	}
	eglMakeCurrent(egl_display, egl_surface, egl_surface, egl_context);
}

void GLManagerFbdev::release_current() {
	if (egl_display == EGL_NO_DISPLAY) {
		return;
	}
	eglMakeCurrent(egl_display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
}

void GLManagerFbdev::swap_buffers() {
	if (fb_mem == nullptr || egl_display == EGL_NO_DISPLAY) {
		return;
	}
	make_current();

	const uint64_t t0 = OS::get_singleton()->get_ticks_usec();
	glFinish();
	glReadPixels(0, 0, render_width, render_height, GL_RGBA, GL_UNSIGNED_BYTE, readback.ptrw());
	glFinish();
	const uint64_t t1 = OS::get_singleton()->get_ticks_usec();

	// Convert the RGBA readback (bottom-up) into the framebuffer layout, flipping
	// vertically and honouring the channel offsets the driver reported. When the
	// panel wants BGRX (the common case on these SoCs) the swizzle is done with
	// 32-bit arithmetic instead of per-channel byte handling.
	// Write straight into the page that will be flipped in, so the frame is
	// swizzled once instead of being converted into a staging buffer and copied.
	int page = 0;
	if (fb_info.panning && page_count > 1) {
		page = (active_page + 1) % page_count;
	}
	uint8_t *dst_base = fb_mem + (size_t)page * (size_t)fb_info.stride * (size_t)fb_info.height;
	const int out_bpp = fb_info.bpp / 8;
	const bool bgrx = out_bpp == 4 && fb_info.r_offset == 16 && fb_info.g_offset == 8 &&
			fb_info.b_offset == 0 && fb_info.r_length == 8 && fb_info.g_length == 8 &&
			fb_info.b_length == 8;

	if (bgrx && render_scale > 1 &&
			render_width * render_scale == fb_info.width &&
			render_height * render_scale == fb_info.height) {
		// Integer upscale: swizzle one source row into a full-width scratch row,
		// then replicate it vertically/horizontally with memcpy and a fast fill.
		const int s = render_scale;
		Vector<uint32_t> scratch;
		scratch.resize(fb_info.width);
		uint32_t *row = scratch.ptrw();
		for (int sy = 0; sy < render_height; sy++) {
			const uint32_t *in = (const uint32_t *)(readback.ptr() + (size_t)(render_height - 1 - sy) * (size_t)render_width * 4);
			for (int x = 0; x < render_width; x++) {
				const uint32_t px = in[x];
				const uint32_t out = 0xff000000u | ((px & 0x000000ffu) << 16) | (px & 0x0000ff00u) | ((px >> 16) & 0x000000ffu);
				row[x * s] = out;
				for (int k = 1; k < s; k++) {
					row[x * s + k] = out;
				}
			}
			uint8_t *dst = dst_base + (size_t)(sy * s) * (size_t)fb_info.stride;
			memcpy(dst, row, (size_t)fb_info.width * 4);
			for (int k = 1; k < s; k++) {
				memcpy(dst + (size_t)k * (size_t)fb_info.stride, row, (size_t)fb_info.width * 4);
			}
		}
	} else {
		for (int y = 0; y < fb_info.height; y++) {
			const int sy = MIN(render_height - 1, y * render_height / fb_info.height);
			const uint8_t *src = readback.ptr() + (size_t)(render_height - 1 - sy) * (size_t)render_width * 4;
			uint8_t *dst = dst_base + (size_t)y * (size_t)fb_info.stride;
			if (bgrx) {
				uint32_t *out = (uint32_t *)dst;
				const uint32_t *in = (const uint32_t *)src;
				for (int x = 0; x < fb_info.width; x++) {
					const int sx = MIN(render_width - 1, x * render_width / fb_info.width);
					const uint32_t px = in[sx];
					out[x] = 0xff000000u | ((px & 0x000000ffu) << 16) | (px & 0x0000ff00u) | ((px >> 16) & 0x000000ffu);
				}
			} else if (out_bpp == 4) {
				for (int x = 0; x < fb_info.width; x++) {
					const int sx = MIN(render_width - 1, x * render_width / fb_info.width);
					const uint8_t *p = src + sx * 4;
					const uint8_t r = p[0], g = p[1], b = p[2];
					uint32_t px = 0;
					px |= (uint32_t)((r >> (8 - fb_info.r_length)) & ((1u << fb_info.r_length) - 1)) << fb_info.r_offset;
					px |= (uint32_t)((g >> (8 - fb_info.g_length)) & ((1u << fb_info.g_length) - 1)) << fb_info.g_offset;
					px |= (uint32_t)((b >> (8 - fb_info.b_length)) & ((1u << fb_info.b_length) - 1)) << fb_info.b_offset;
					((uint32_t *)dst)[x] = px | 0xff000000u;
				}
			} else if (out_bpp == 2) {
				for (int x = 0; x < fb_info.width; x++) {
					const int sx = MIN(render_width - 1, x * render_width / fb_info.width);
					const uint8_t *p = src + sx * 4;
					((uint16_t *)dst)[x] = (uint16_t)(((p[0] >> 3) << 11) | ((p[1] >> 2) << 5) | (p[2] >> 3));
				}
			}
		}
	}
	const uint64_t t2 = OS::get_singleton()->get_ticks_usec();

	// Present: the page was filled above, so only the flip is left. FB_ACTIVATE_NOW
	// keeps this from blocking a full vblank on top of the (already slow) readback;
	// the three-page rotation absorbs the tear that a non-vsynced flip may show.
	if (fb_info.panning && page_count > 1) {
		struct fb_var_screeninfo var;
		if (ioctl(fb_fd, FBIOGET_VSCREENINFO, &var) == 0) {
			var.yoffset = (uint32_t)page * (uint32_t)fb_info.height;
			var.activate = FB_ACTIVATE_NOW;
			if (ioctl(fb_fd, FBIOPAN_DISPLAY, &var) == 0) {
				active_page = page;
			}
		}
	}

	const uint64_t t3 = OS::get_singleton()->get_ticks_usec();

	stat_frames++;
	stat_read_us += t1 - t0;
	stat_convert_us += t2 - t1;
	stat_blit_us += t3 - t2;
	if (fbdev_debug_enabled() && t3 - stat_last_report_us >= 1000000) {
		fprintf(stderr, "fbdev-trace: %.1f fps | read %.1f ms, convert %.1f ms, blit %.1f ms\n",
				(double)stat_frames * 1000000.0 / (double)(t3 - stat_last_report_us),
				(double)stat_read_us / (double)stat_frames / 1000.0,
				(double)stat_convert_us / (double)stat_frames / 1000.0,
				(double)stat_blit_us / (double)stat_frames / 1000.0);
		fflush(stderr);
		stat_frames = 0;
		stat_read_us = 0;
		stat_convert_us = 0;
		stat_blit_us = 0;
		stat_last_report_us = t3;
	}
}

GLManagerFbdev::~GLManagerFbdev() {
	shutdown();
}

#endif // FBDEV_ENABLED
