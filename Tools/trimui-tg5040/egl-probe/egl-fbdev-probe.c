/*
 * egl-fbdev-probe — find the EGL path that can present to /dev/fb0 on this SoC.
 *
 * Cross-compiled for aarch64 and run on the handheld. It dlopen()s the vendor
 * EGL/GLES libraries so it carries no link-time dependency on them, then tries,
 * in order:
 *   1. window surface from an fbdev_window handle (ARM Mali FBDEV platform)
 *   2. window surface from EGL_DEFAULT_DISPLAY with a NULL native window
 *   3. pbuffer surface (renders offscreen, caller blits)
 *
 * Each successful path clears the screen to a distinct colour so the panel (or
 * a framebuffer grab) shows which one worked.
 *
 * Build: aarch64-linux-gnu-gcc -O2 -o egl-fbdev-probe egl-fbdev-probe.c -ldl
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <fcntl.h>
#include <signal.h>
#include <linux/fb.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <unistd.h>

typedef void *EGLDisplay;
typedef void *EGLConfig;
typedef void *EGLContext;
typedef void *EGLSurface;
typedef int32_t EGLint;
typedef uint32_t EGLBoolean;
typedef uint32_t EGLenum;
typedef void *EGLNativeDisplayType;
typedef void *EGLNativeWindowType;

#define EGL_DEFAULT_DISPLAY ((EGLNativeDisplayType)0)
#define EGL_NO_DISPLAY ((EGLDisplay)0)
#define EGL_NO_CONTEXT ((EGLContext)0)
#define EGL_NO_SURFACE ((EGLSurface)0)
#define EGL_FALSE 0
#define EGL_TRUE 1
#define EGL_SUCCESS 0x3000
#define EGL_NONE 0x3038
#define EGL_RENDERABLE_TYPE 0x3040
#define EGL_SURFACE_TYPE 0x3033
#define EGL_WINDOW_BIT 0x0004
#define EGL_PBUFFER_BIT 0x0001
#define EGL_OPENGL_ES2_BIT 0x0004
#define EGL_OPENGL_ES3_BIT 0x0040
#define EGL_RED_SIZE 0x3024
#define EGL_GREEN_SIZE 0x3023
#define EGL_BLUE_SIZE 0x3022
#define EGL_ALPHA_SIZE 0x3021
#define EGL_DEPTH_SIZE 0x3025
#define EGL_STENCIL_SIZE 0x3026
#define EGL_WIDTH 0x3057
#define EGL_HEIGHT 0x3056
#define EGL_CONTEXT_CLIENT_VERSION 0x3098
#define EGL_VENDOR 0x3053
#define EGL_VERSION 0x3054
#define EGL_EXTENSIONS 0x3055
#define EGL_PLATFORM_FBDEV_EXT 0x313D

typedef EGLDisplay (*fn_eglGetDisplay)(EGLNativeDisplayType);
typedef EGLDisplay (*fn_eglGetPlatformDisplayEXT)(EGLint, void *, const EGLint *);
typedef EGLBoolean (*fn_eglInitialize)(EGLDisplay, EGLint *, EGLint *);
typedef EGLBoolean (*fn_eglChooseConfig)(EGLDisplay, const EGLint *, EGLConfig *, EGLint, EGLint *);
typedef EGLSurface (*fn_eglCreateWindowSurface)(EGLDisplay, EGLConfig, EGLNativeWindowType, const EGLint *);
typedef EGLSurface (*fn_eglCreatePbufferSurface)(EGLDisplay, EGLConfig, const EGLint *);
typedef EGLContext (*fn_eglCreateContext)(EGLDisplay, EGLConfig, EGLContext, const EGLint *);
typedef EGLBoolean (*fn_eglMakeCurrent)(EGLDisplay, EGLSurface, EGLSurface, EGLContext);
typedef EGLBoolean (*fn_eglSwapBuffers)(EGLDisplay, EGLSurface);
typedef EGLint (*fn_eglGetError)(void);
typedef const char *(*fn_eglQueryString)(EGLDisplay, EGLint);
typedef EGLBoolean (*fn_eglTerminate)(EGLDisplay);

typedef void (*fn_glClearColor)(float, float, float, float);
typedef void (*fn_glClear)(uint32_t);
typedef void (*fn_glFinish)(void);
typedef void (*fn_glReadPixels)(int, int, int, int, uint32_t, uint32_t, void *);
typedef const unsigned char *(*fn_glGetString)(uint32_t);

static void *handle_egl;
static void *handle_gles;

static fn_eglGetDisplay p_eglGetDisplay;
static fn_eglGetPlatformDisplayEXT p_eglGetPlatformDisplayEXT;
static fn_eglInitialize p_eglInitialize;
static fn_eglChooseConfig p_eglChooseConfig;
static fn_eglCreateWindowSurface p_eglCreateWindowSurface;
static fn_eglCreatePbufferSurface p_eglCreatePbufferSurface;
static fn_eglCreateContext p_eglCreateContext;
static fn_eglMakeCurrent p_eglMakeCurrent;
static fn_eglSwapBuffers p_eglSwapBuffers;
static fn_eglGetError p_eglGetError;
static fn_eglQueryString p_eglQueryString;
static fn_eglTerminate p_eglTerminate;

static fn_glClearColor p_glClearColor;
static fn_glClear p_glClear;
static fn_glFinish p_glFinish;
static fn_glReadPixels p_glReadPixels;
static fn_glGetString p_glGetString;

struct fb_state {
    int fd;
    struct fb_fix_screeninfo fix;
    struct fb_var_screeninfo var;
    uint8_t *mem;
    size_t size;
};

static struct fb_state fb = { -1, { 0 }, { 0 }, NULL, 0 };

struct fbdev_window {
    unsigned short width;
    unsigned short height;
};

static void *sym(void *handle, const char *name) {
    void *p = dlsym(handle, name);
    if (!p) {
        fprintf(stderr, "probe: missing symbol %s: %s\n", name, dlerror());
    }
    return p;
}

static int load_egl(void) {
    const char *candidates[] = { "libEGL.so.1", "libEGL.so", "/usr/lib/libEGL.so.1", NULL };
    for (int i = 0; candidates[i] && !handle_egl; i++) {
        handle_egl = dlopen(candidates[i], RTLD_NOW | RTLD_GLOBAL);
        if (handle_egl) {
            printf("probe: loaded EGL from %s\n", candidates[i]);
        }
    }
    if (!handle_egl) {
        fprintf(stderr, "probe: cannot load libEGL: %s\n", dlerror());
        return -1;
    }
    p_eglGetDisplay = (fn_eglGetDisplay)sym(handle_egl, "eglGetDisplay");
    p_eglGetPlatformDisplayEXT = (fn_eglGetPlatformDisplayEXT)sym(handle_egl, "eglGetPlatformDisplayEXT");
    p_eglInitialize = (fn_eglInitialize)sym(handle_egl, "eglInitialize");
    p_eglChooseConfig = (fn_eglChooseConfig)sym(handle_egl, "eglChooseConfig");
    p_eglCreateWindowSurface = (fn_eglCreateWindowSurface)sym(handle_egl, "eglCreateWindowSurface");
    p_eglCreatePbufferSurface = (fn_eglCreatePbufferSurface)sym(handle_egl, "eglCreatePbufferSurface");
    p_eglCreateContext = (fn_eglCreateContext)sym(handle_egl, "eglCreateContext");
    p_eglMakeCurrent = (fn_eglMakeCurrent)sym(handle_egl, "eglMakeCurrent");
    p_eglSwapBuffers = (fn_eglSwapBuffers)sym(handle_egl, "eglSwapBuffers");
    p_eglGetError = (fn_eglGetError)sym(handle_egl, "eglGetError");
    p_eglQueryString = (fn_eglQueryString)sym(handle_egl, "eglQueryString");
    p_eglTerminate = (fn_eglTerminate)sym(handle_egl, "eglTerminate");
    if (!p_eglGetDisplay || !p_eglInitialize || !p_eglChooseConfig || !p_eglCreateContext || !p_eglMakeCurrent) {
        return -1;
    }
    return 0;
}

static int load_gles(void) {
    const char *candidates[] = { "libGLESv2.so.2", "libGLESv2.so", "/usr/lib/libGLESv2.so.2", NULL };
    for (int i = 0; candidates[i] && !handle_gles; i++) {
        handle_gles = dlopen(candidates[i], RTLD_NOW | RTLD_GLOBAL);
        if (handle_gles) {
            printf("probe: loaded GLES from %s\n", candidates[i]);
        }
    }
    if (!handle_gles) {
        fprintf(stderr, "probe: cannot load libGLESv2: %s\n", dlerror());
        return -1;
    }
    p_glClearColor = (fn_glClearColor)sym(handle_gles, "glClearColor");
    p_glClear = (fn_glClear)sym(handle_gles, "glClear");
    p_glFinish = (fn_glFinish)sym(handle_gles, "glFinish");
    p_glReadPixels = (fn_glReadPixels)sym(handle_gles, "glReadPixels");
    p_glGetString = (fn_glGetString)sym(handle_gles, "glGetString");
    return (p_glClearColor && p_glClear) ? 0 : -1;
}

static int fb_open(void) {
    const char *nodes[] = { "/dev/fb0", "/dev/fb/0", NULL };
    for (int i = 0; nodes[i]; i++) {
        fb.fd = open(nodes[i], O_RDWR);
        if (fb.fd >= 0) {
            printf("probe: opened %s\n", nodes[i]);
            break;
        }
    }
    if (fb.fd < 0) {
        perror("probe: open fb");
        return -1;
    }
    if (ioctl(fb.fd, FBIOGET_FSCREENINFO, &fb.fix) < 0) {
        perror("probe: FBIOGET_FSCREENINFO");
        return -1;
    }
    if (ioctl(fb.fd, FBIOGET_VSCREENINFO, &fb.var) < 0) {
        perror("probe: FBIOGET_VSCREENINFO");
        return -1;
    }
    printf("probe: fb %ux%u virt=%ux%u bpp=%u line=%u\n",
            fb.var.xres, fb.var.yres, fb.var.xres_virtual, fb.var.yres_virtual,
            fb.var.bits_per_pixel, fb.fix.line_length);
    fb.size = (size_t)fb.fix.line_length * fb.var.yres_virtual;
    if (fb.size == 0) {
        fb.size = (size_t)fb.fix.line_length * fb.var.yres;
    }
    fb.mem = (uint8_t *)mmap(NULL, fb.size, PROT_READ | PROT_WRITE, MAP_SHARED, fb.fd, 0);
    if (fb.mem == MAP_FAILED) {
        perror("probe: mmap fb");
        fb.mem = NULL;
        return -1;
    }
    return 0;
}

static void fb_fill(uint32_t argb) {
    if (!fb.mem) {
        return;
    }
    uint32_t px = argb;
    if (fb.var.bits_per_pixel == 16) {
        uint32_t r = (argb >> 16) & 0xff, g = (argb >> 8) & 0xff, b = argb & 0xff;
        px = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
    }
    size_t count = (size_t)fb.var.xres * fb.var.yres;
    if (fb.var.bits_per_pixel == 32) {
        for (size_t i = 0; i < count; i++) {
            ((uint32_t *)fb.mem)[i] = px;
        }
    } else if (fb.var.bits_per_pixel == 16) {
        for (size_t i = 0; i < count; i++) {
            ((uint16_t *)fb.mem)[i] = (uint16_t)px;
        }
    }
    msync(fb.mem, fb.size, MS_SYNC);
}

static EGLConfig pick_config(EGLDisplay dpy, EGLint surface_bit) {
    EGLint attribs[] = {
        EGL_SURFACE_TYPE, surface_bit,
        EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT,
        EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8,
        EGL_DEPTH_SIZE, 24, EGL_STENCIL_SIZE, 8,
        EGL_NONE
    };
    EGLConfig config = NULL;
    EGLint count = 0;
    if (!p_eglChooseConfig(dpy, attribs, &config, 1, &count) || count < 1) {
        return NULL;
    }
    return config;
}

static int try_path(const char *label, EGLDisplay dpy, EGLSurface surface, EGLConfig config, uint32_t colour) {
    EGLint ctx_attribs[] = { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE };
    EGLContext ctx = p_eglCreateContext(dpy, config, EGL_NO_CONTEXT, ctx_attribs);
    if (ctx == EGL_NO_CONTEXT) {
        printf("probe: %s: eglCreateContext failed 0x%x\n", label, p_eglGetError ? p_eglGetError() : 0);
        return -1;
    }
    if (!p_eglMakeCurrent(dpy, surface, surface, ctx)) {
        printf("probe: %s: eglMakeCurrent failed 0x%x\n", label, p_eglGetError ? p_eglGetError() : 0);
        return -1;
    }
    const unsigned char *ver = p_glGetString ? p_glGetString(0x1F02) : NULL;
    printf("probe: %s: OK GL_VERSION=%s\n", label, ver ? (const char *)ver : "?");
    p_glClearColor((float)((colour >> 16) & 0xff) / 255.0f, (float)((colour >> 8) & 0xff) / 255.0f,
            (float)(colour & 0xff) / 255.0f, 1.0f);
    p_glClear(0x4000);
    if (p_glFinish) {
        p_glFinish();
    }
    if (p_eglSwapBuffers && surface) {
        p_eglSwapBuffers(dpy, surface);
    }
    printf("probe: %s: colour 0x%06x presented\n", label, colour);
    return 0;
}

static void on_crash(int sig) {
    const char *msg = "probe: CRASH signal\n";
    ssize_t ignored = write(2, msg, strlen(msg));
    (void)ignored;
    _exit(70 + sig);
}

int main(int argc, char **argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
    signal(SIGSEGV, on_crash);
    signal(SIGBUS, on_crash);
    signal(SIGABRT, on_crash);
    int dwell = 6;
    if (argc > 1) {
        dwell = atoi(argv[1]);
    }
    if (fb_open() < 0) {
        return 1;
    }
    if (getenv("PROBE_MODE") && strcmp(getenv("PROBE_MODE"), "fill") == 0) {
        for (int i = 0; i < 2; i++) {
            fb_fill(0xff00ff);
            printf("probe: fb-fill page %d (magenta)\n", i);
        }
        sleep(dwell);
        return 0;
    }
    if (load_egl() < 0 || load_gles() < 0) {
        return 1;
    }

    EGLDisplay dpy = p_eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (dpy == EGL_NO_DISPLAY) {
        printf("probe: eglGetDisplay(default) returned no display\n");
        return 1;
    }
    if (!p_eglInitialize(dpy, NULL, NULL)) {
        printf("probe: eglInitialize failed 0x%x\n", p_eglGetError ? p_eglGetError() : 0);
        return 1;
    }
    printf("probe: EGL vendor=%s version=%s\n",
            p_eglQueryString(dpy, EGL_VENDOR) ? p_eglQueryString(dpy, EGL_VENDOR) : "?",
            p_eglQueryString(dpy, EGL_VERSION) ? p_eglQueryString(dpy, EGL_VERSION) : "?");
    const char *ext = p_eglQueryString(dpy, EGL_EXTENSIONS);
    printf("probe: EGL extensions=%s\n", ext ? ext : "(none)");

    static struct fbdev_window window;
    window.width = (unsigned short)fb.var.xres;
    window.height = (unsigned short)fb.var.yres;

    /* 1. fbdev window surface (ARM Mali's FBDEV platform). */
    EGLConfig config = pick_config(dpy, EGL_WINDOW_BIT);
    if (config) {
        EGLSurface surface = p_eglCreateWindowSurface(dpy, config, (EGLNativeWindowType)&window, NULL);
        if (surface != EGL_NO_SURFACE) {
            if (try_path("fbdev-window", dpy, surface, config, 0x00ff00) == 0) {
                sleep(dwell);
                fprintf(stderr, "probe: RESULT=fbdev-window\n");
                return 0;
            }
        } else {
            printf("probe: fbdev-window: eglCreateWindowSurface failed 0x%x\n", p_eglGetError ? p_eglGetError() : 0);
        }
    } else {
        printf("probe: no EGL config with EGL_WINDOW_BIT\n");
    }

    /* 2. window surface with a NULL native window. */
    if (config) {
        EGLSurface surface = p_eglCreateWindowSurface(dpy, config, NULL, NULL);
        if (surface != EGL_NO_SURFACE) {
            if (try_path("null-window", dpy, surface, config, 0x0000ff) == 0) {
                sleep(dwell);
                fprintf(stderr, "probe: RESULT=null-window\n");
                return 0;
            }
        } else {
            printf("probe: null-window: eglCreateWindowSurface failed 0x%x\n", p_eglGetError ? p_eglGetError() : 0);
        }
    }

    /* 3. pbuffer surface, then blit to fb0 ourselves. */
    config = pick_config(dpy, EGL_PBUFFER_BIT);
    if (config && p_eglCreatePbufferSurface) {
        EGLint pb_attribs[] = { EGL_WIDTH, (EGLint)fb.var.xres, EGL_HEIGHT, (EGLint)fb.var.yres, EGL_NONE };
        EGLSurface surface = p_eglCreatePbufferSurface(dpy, config, pb_attribs);
        if (surface != EGL_NO_SURFACE) {
            EGLint ctx_attribs[] = { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE };
            EGLContext ctx = p_eglCreateContext(dpy, config, EGL_NO_CONTEXT, ctx_attribs);
            if (ctx != EGL_NO_CONTEXT && p_eglMakeCurrent(dpy, surface, surface, ctx)) {
                p_glClearColor(1.0f, 0.5f, 0.0f, 1.0f);
                p_glClear(0x4000);
                if (p_glFinish) {
                    p_glFinish();
                }
                uint32_t w = fb.var.xres, h = fb.var.yres;
                uint32_t *pixels = (uint32_t *)malloc((size_t)w * h * 4);
                if (pixels && p_glReadPixels) {
                    p_glReadPixels(0, 0, (int)w, (int)h, 0x1908 /*GL_RGBA*/, 0x1401 /*GL_UNSIGNED_BYTE*/, pixels);
                    for (uint32_t i = 0; i < w * h; i++) {
                        uint32_t p = pixels[i];
                        ((uint32_t *)fb.mem)[i] = (p & 0xff00ff00u) | ((p >> 16) & 0xff) | ((p & 0xff) << 16);
                    }
                    msync(fb.mem, fb.size, MS_SYNC);
                    printf("probe: pbuffer: readback blit done\n");
                    sleep(dwell);
                    fprintf(stderr, "probe: RESULT=pbuffer\n");
                    return 0;
                }
            } else {
                printf("probe: pbuffer: context/current failed\n");
            }
        } else {
            printf("probe: pbuffer: eglCreatePbufferSurface failed 0x%x\n", p_eglGetError ? p_eglGetError() : 0);
        }
    }

    /* 4. Nothing worked through EGL: still prove the framebuffer path. */
    fb_fill(0xff00ff);
    printf("probe: fb-fill only (no EGL path), colour magenta\n");
    sleep(dwell);
    fprintf(stderr, "probe: RESULT=fb-fill\n");
    return 0;
}
