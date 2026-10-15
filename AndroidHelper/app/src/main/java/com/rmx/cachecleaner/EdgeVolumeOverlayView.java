package com.rmx.cachecleaner;

import android.content.Context;
import android.content.res.Configuration;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.RectF;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import java.util.Collections;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;

/**
 * Ultra-lightweight, single-view overlay for Edge Volume Control.
 *
 * Replaces the custom volume panel with a tiny, persistent right-edge handle:
 * 1. Zero child views, zero XML inflation, zero ViewGroups.
 * 2. Draws a rounded charcoal pill directly on Canvas.
 * 3. Compact window (36dp x 84dp touch zone), never resizes to full screen.
 * 4. Inward swipe or tap requests native ColorOS/Android volume slider.
 * 5. Supports vertical drag repositioning and persists vertical ratio.
 * 6. Debounced via EdgeVolumeController to prevent duplicate invocations.
 */
public class EdgeVolumeOverlayView extends View {

    private static final String TAG = "EdgeVolumeOverlay";

    private static final int PILL_COLOR_RGB = 0x2A2A2A; // Charcoal
    private static final long LONG_PRESS_TIMEOUT_MS = 350L;

    private final WindowManager windowManager;
    private final WindowManager.LayoutParams windowParams;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private final Paint pillPaint;
    private final RectF pillRect = new RectF();

    // Dimensions in pixels
    private final int handleTouchWidth;
    private final int handleTouchHeight;
    private final int pillWidth;
    private final int pillHeight;
    private final float pillRadius;
    private final int touchSlop;
    private final int swipeThreshold;
    private final int edgeMargin;

    private int screenWidth;
    private int screenHeight;

    // Gesture tracking
    private float initialTouchX;
    private float initialTouchY;
    private int initialWindowY;
    private boolean isRepositioning = false;
    private boolean isSwipeTriggered = false;
    private boolean isTouchActive = false;

    private final Runnable longPressRunnable = new Runnable() {
        @Override
        public void run() {
            if (isTouchActive && !isSwipeTriggered) {
                isRepositioning = true;
                performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);
                invalidate();
            }
        }
    };

    public EdgeVolumeOverlayView(Context context, WindowManager wm) {
        super(context);
        this.windowManager = wm;

        DisplayMetrics dm = context.getResources().getDisplayMetrics();
        screenWidth = dm.widthPixels;
        screenHeight = dm.heightPixels;

        handleTouchWidth = dpToPx(36, dm);
        handleTouchHeight = dpToPx(84, dm);
        pillWidth = dpToPx(6, dm);
        pillHeight = dpToPx(48, dm);
        pillRadius = pillWidth / 2.0f;
        edgeMargin = dpToPx(56, dm); // Safe margin for status bar / nav bar

        ViewConfiguration vc = ViewConfiguration.get(context);
        touchSlop = vc.getScaledTouchSlop();
        swipeThreshold = dpToPx(16, dm);

        pillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        pillPaint.setStyle(Paint.Style.FILL);
        updatePaintAlpha(false);

        windowParams = createLayoutParams(context);
    }

    private int dpToPx(int dp, DisplayMetrics dm) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, dp, dm);
    }

    private WindowManager.LayoutParams createLayoutParams(Context context) {
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                handleTouchWidth,
                handleTouchHeight,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
                        | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT
        );

        params.gravity = Gravity.TOP | Gravity.END;
        params.x = 0;

        float savedRatio = EdgeVolumePreferences.getHandleYRatio(context);
        int targetY = (int) (screenHeight * savedRatio - handleTouchHeight / 2.0f);
        params.y = clampY(targetY);

        return params;
    }

    public WindowManager.LayoutParams getWindowParams() {
        return windowParams;
    }

    private int clampY(int y) {
        int minY = edgeMargin;
        int maxY = Math.max(minY, screenHeight - handleTouchHeight - edgeMargin);
        return Math.max(minY, Math.min(maxY, y));
    }

    private void updatePaintAlpha(boolean active) {
        float idleOpacity = EdgeVolumePreferences.getHandleOpacity(getContext());
        float targetOpacity;
        if (active || isRepositioning) {
            targetOpacity = Math.max(0.40f, Math.min(0.85f, idleOpacity * 2.5f));
        } else {
            targetOpacity = idleOpacity;
        }
        int alpha = (int) (targetOpacity * 255.0f);
        pillPaint.setColor((alpha << 24) | PILL_COLOR_RGB);
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        super.onSizeChanged(w, h, oldw, oldh);
        float right = w;
        float left = right - pillWidth;
        float top = (h - pillHeight) / 2.0f;
        float bottom = top + pillHeight;
        pillRect.set(left, top, right, bottom);
        updateGestureExclusion(w, h);
    }

    @Override
    protected void onLayout(boolean changed, int left, int top, int right, int bottom) {
        super.onLayout(changed, left, top, right, bottom);
        updateGestureExclusion(right - left, bottom - top);
    }

    private void updateGestureExclusion(int w, int h) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && w > 0 && h > 0) {
            try {
                setSystemGestureExclusionRects(Collections.singletonList(new Rect(0, 0, w, h)));
            } catch (Throwable ignored) {}
        }
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        canvas.drawRoundRect(pillRect, pillRadius, pillRadius, pillPaint);
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                initialTouchX = event.getRawX();
                initialTouchY = event.getRawY();
                initialWindowY = windowParams.y;
                isRepositioning = false;
                isSwipeTriggered = false;
                isTouchActive = true;

                updatePaintAlpha(true);
                invalidate();

                mainHandler.postDelayed(longPressRunnable, LONG_PRESS_TIMEOUT_MS);
                return true;

            case MotionEvent.ACTION_MOVE:
                float deltaX = initialTouchX - event.getRawX(); // positive when dragging inward (left)
                float deltaY = event.getRawY() - initialTouchY;

                if (!isRepositioning && !isSwipeTriggered) {
                    // Check for inward swipe (leftward gesture)
                    if (deltaX > swipeThreshold && deltaX > Math.abs(deltaY)) {
                        isSwipeTriggered = true;
                        mainHandler.removeCallbacks(longPressRunnable);
                        performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);
                        EdgeVolumeController.triggerSystemVolumeUI(getContext());
                        return true;
                    }

                    // Check for vertical drag beyond touch slop
                    if (Math.abs(deltaY) > touchSlop) {
                        mainHandler.removeCallbacks(longPressRunnable);
                        isRepositioning = true;
                        invalidate();
                    }
                }

                if (isRepositioning) {
                    int newY = clampY(initialWindowY + (int) deltaY);
                    if (newY != windowParams.y) {
                        windowParams.y = newY;
                        try {
                            windowManager.updateViewLayout(this, windowParams);
                        } catch (Throwable t) {
                            Log.w(TAG, "Error updating handle layout during drag: " + t.getMessage());
                        }
                    }
                    return true;
                }
                return true;

            case MotionEvent.ACTION_UP:
                mainHandler.removeCallbacks(longPressRunnable);
                isTouchActive = false;

                if (isRepositioning) {
                    // Save new vertical position ratio
                    float usableHeight = Math.max(1, screenHeight - handleTouchHeight);
                    float ratio = (float) windowParams.y / usableHeight;
                    EdgeVolumePreferences.setHandleYRatio(getContext(), ratio);
                    isRepositioning = false;
                } else if (!isSwipeTriggered) {
                    float inwardDelta = initialTouchX - event.getRawX();
                    float tapDeltaX = Math.abs(event.getRawX() - initialTouchX);
                    float tapDeltaY = Math.abs(event.getRawY() - initialTouchY);
                    if ((tapDeltaX < touchSlop && tapDeltaY < touchSlop)
                            || (inwardDelta > swipeThreshold && inwardDelta > Math.abs(event.getRawY() - initialTouchY))) {
                        performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);
                        EdgeVolumeController.triggerSystemVolumeUI(getContext());
                    }
                }

                updatePaintAlpha(false);
                invalidate();
                return true;

            case MotionEvent.ACTION_CANCEL:
                mainHandler.removeCallbacks(longPressRunnable);
                isTouchActive = false;
                isRepositioning = false;
                updatePaintAlpha(false);
                invalidate();
                return true;
        }
        return super.onTouchEvent(event);
    }

    @Override
    protected void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        DisplayMetrics dm = getContext().getResources().getDisplayMetrics();
        screenWidth = dm.widthPixels;
        screenHeight = dm.heightPixels;

        float savedRatio = EdgeVolumePreferences.getHandleYRatio(getContext());
        int targetY = (int) (screenHeight * savedRatio - handleTouchHeight / 2.0f);
        windowParams.y = clampY(targetY);

        try {
            windowManager.updateViewLayout(this, windowParams);
        } catch (Throwable t) {
            Log.w(TAG, "Error updating handle on config change: " + t.getMessage());
        }
    }

    public void resetHandlePosition() {
        int targetY = clampY((screenHeight - handleTouchHeight) / 2);
        windowParams.y = targetY;
        EdgeVolumePreferences.resetPosition(getContext());
        try {
            windowManager.updateViewLayout(this, windowParams);
        } catch (Throwable t) {
            Log.w(TAG, "Error resetting handle layout: " + t.getMessage());
        }
    }

    public void updateOpacity() {
        updatePaintAlpha(false);
        invalidate();
    }

    public void cleanup() {
        mainHandler.removeCallbacksAndMessages(null);
    }
}
