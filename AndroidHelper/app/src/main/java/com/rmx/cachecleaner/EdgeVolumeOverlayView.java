package com.rmx.cachecleaner;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.content.Context;
import android.content.res.Configuration;
import android.graphics.PixelFormat;
import android.media.AudioManager;
import android.os.Handler;
import android.os.Looper;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.LayoutInflater;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.SeekBar;
import android.widget.TextView;

import java.util.Locale;

/**
 * Composite overlay view that manages:
 * 1. The near-transparent collapsed edge handle on the right side of the screen.
 * 2. Gestures (inward swipe to expand, vertical drag to reposition, long-press to enter reposition mode).
 * 3. The floating expanded volume panel with Media, Ring, and Alarm controls.
 * 4. Outside-touch dismissal and auto-retract timeout with slider touch guards.
 * 5. WindowManager LayoutParams toggling between compact handle and full-screen touch scrim.
 */
public class EdgeVolumeOverlayView extends FrameLayout {

    private static final String TAG = "EdgeVolumeOverlay";
    private static final long AUTO_COLLAPSE_DELAY_MS = 5000;
    private static final long LONG_PRESS_TIMEOUT_MS = 350;

    private final WindowManager windowManager;
    private final EdgeVolumeController volumeController;
    private WindowManager.LayoutParams windowParams;

    // View References
    private View viewScrim;
    private LinearLayout llVolumePanel;
    private FrameLayout flHandleContainer;
    private View vHandlePill;

    // Media Controls
    private ImageView ivMediaIcon;
    private TextView tvMediaValue;
    private ImageView btnMediaMute;
    private SeekBar seekMedia;

    // Ring Controls
    private TextView tvRingValue;
    private SeekBar seekRing;

    // Alarm Controls
    private TextView tvAlarmValue;
    private SeekBar seekAlarm;

    // Handler & Runnables
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Runnable autoCollapseRunnable = new Runnable() {
        @Override
        public void run() {
            if (isExpanded && !isUserDraggingSlider) {
                collapsePanel();
            }
        }
    };

    private final Runnable longPressRunnable = new Runnable() {
        @Override
        public void run() {
            if (!isExpanded) {
                enterRepositionMode();
            }
        }
    };

    // State Variables
    private boolean isExpanded = false;
    private boolean isRepositioning = false;
    private boolean isDraggingHandle = false;
    private boolean isUserDraggingSlider = false;

    // Sizing & Coordinates
    private int screenWidth;
    private int screenHeight;
    private int handleTouchWidth;
    private int handleTouchHeight;
    private int touchSlop;
    private int swipeThreshold;
    private int currentHandleY = 0;

    // Gesture Tracking
    private float initialTouchX;
    private float initialTouchY;
    private int initialHandleY;
    private long touchDownTime;

    public EdgeVolumeOverlayView(Context context, WindowManager wm, EdgeVolumeController controller) {
        super(context);
        this.windowManager = wm;
        this.volumeController = controller;

        init(context);
    }

    private void init(Context context) {
        LayoutInflater.from(context).inflate(R.layout.view_edge_volume_overlay, this, true);

        // Find Views
        viewScrim = findViewById(R.id.view_scrim);
        llVolumePanel = findViewById(R.id.ll_volume_panel);
        flHandleContainer = findViewById(R.id.fl_handle_container);
        vHandlePill = findViewById(R.id.v_handle_pill);

        ivMediaIcon = findViewById(R.id.iv_media_icon);
        tvMediaValue = findViewById(R.id.tv_media_value);
        btnMediaMute = findViewById(R.id.btn_media_mute);
        seekMedia = findViewById(R.id.seek_media);

        tvRingValue = findViewById(R.id.tv_ring_value);
        seekRing = findViewById(R.id.seek_ring);

        tvAlarmValue = findViewById(R.id.tv_alarm_value);
        seekAlarm = findViewById(R.id.seek_alarm);

        ImageView btnPanelClose = findViewById(R.id.btn_panel_close);
        if (btnPanelClose != null) {
            btnPanelClose.setOnClickListener(new OnClickListener() {
                @Override
                public void onClick(View v) {
                    collapsePanel();
                }
            });
        }

        // Tap outside scrim collapses panel
        viewScrim.setOnClickListener(new OnClickListener() {
            @Override
            public void onClick(View v) {
                collapsePanel();
            }
        });

        // Compute metrics and dimensions
        updateScreenMetrics();
        handleTouchWidth = dpToPx(36);
        handleTouchHeight = dpToPx(84);
        touchSlop = ViewConfiguration.get(context).getScaledTouchSlop();
        swipeThreshold = dpToPx(24);

        // Compute initial handle Y from persisted ratio
        float savedRatio = EdgeVolumePreferences.getHandleYRatio(context);
        currentHandleY = clampHandleY((int) (screenHeight * savedRatio) - handleTouchHeight / 2);

        // Apply idle opacity to pill
        updatePillOpacity();

        // Setup touch handling for handle
        setupHandleTouchListener();

        // Setup seekbars
        setupSeekBars();

        // Setup Mute button
        btnMediaMute.setOnClickListener(new OnClickListener() {
            @Override
            public void onClick(View v) {
                Log.i(TAG, "btnMediaMute clicked");
                volumeController.toggleMediaMute();
                syncVolumeViews();
                postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        syncVolumeViews();
                    }
                }, 60);
                scheduleAutoCollapse();
            }
        });
    }

    private void updateScreenMetrics() {
        DisplayMetrics dm = getResources().getDisplayMetrics();
        screenWidth = dm.widthPixels;
        screenHeight = dm.heightPixels;
    }

    private int dpToPx(int dp) {
        return Math.round(TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP, dp, getResources().getDisplayMetrics()));
    }

    public void updatePillOpacity() {
        if (vHandlePill != null && !isRepositioning) {
            float opacity = EdgeVolumePreferences.getHandleOpacity(getContext());
            vHandlePill.setAlpha(opacity);
        }
    }

    public void resetHandlePosition() {
        EdgeVolumePreferences.resetPosition(getContext());
        float ratio = EdgeVolumePreferences.getHandleYRatio(getContext());
        currentHandleY = clampHandleY((int) (screenHeight * ratio) - handleTouchHeight / 2);
        if (!isExpanded && windowParams != null) {
            windowParams.y = currentHandleY;
            try {
                windowManager.updateViewLayout(this, windowParams);
            } catch (Throwable ignored) {}
        }
    }

    private int clampHandleY(int targetY) {
        int minY = dpToPx(56); // Below status bar
        int maxY = screenHeight - handleTouchHeight - dpToPx(64); // Above navigation area
        return Math.max(minY, Math.min(maxY, targetY));
    }

    // =========================================================================
    // WindowManager Layout Params Configuration
    // =========================================================================

    public WindowManager.LayoutParams createCollapsedLayoutParams() {
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
        params.y = currentHandleY;
        this.windowParams = params;
        return params;
    }

    private void updateWindowToExpanded() {
        if (windowParams == null) return;
        windowParams.width = WindowManager.LayoutParams.MATCH_PARENT;
        windowParams.height = WindowManager.LayoutParams.MATCH_PARENT;
        windowParams.gravity = Gravity.FILL;
        windowParams.x = 0;
        windowParams.y = 0;
        windowParams.flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN;
        try {
            windowManager.updateViewLayout(this, windowParams);
        } catch (Throwable t) {
            Log.e(TAG, "Error updating window to expanded: " + t.getMessage());
        }
    }

    private void updateWindowToCollapsed() {
        if (windowParams == null) return;
        windowParams.width = handleTouchWidth;
        windowParams.height = handleTouchHeight;
        windowParams.gravity = Gravity.TOP | Gravity.END;
        windowParams.x = 0;
        windowParams.y = currentHandleY;
        windowParams.flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
                | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS;
        try {
            windowManager.updateViewLayout(this, windowParams);
        } catch (Throwable t) {
            Log.e(TAG, "Error updating window to collapsed: " + t.getMessage());
        }
    }

    // =========================================================================
    // Gesture & Handle Touch Handling
    // =========================================================================

    private void setupHandleTouchListener() {
        flHandleContainer.setOnTouchListener(new OnTouchListener() {
            @Override
            public boolean onTouch(View v, MotionEvent event) {
                switch (event.getActionMasked()) {
                    case MotionEvent.ACTION_DOWN:
                        initialTouchX = event.getRawX();
                        initialTouchY = event.getRawY();
                        initialHandleY = currentHandleY;
                        touchDownTime = System.currentTimeMillis();
                        isDraggingHandle = false;
                        isRepositioning = false;

                        // Temporarily increase visibility during touch
                        vHandlePill.setAlpha(0.65f);

                        // Schedule long press for repositioning mode
                        mainHandler.postDelayed(longPressRunnable, LONG_PRESS_TIMEOUT_MS);
                        return true;

                    case MotionEvent.ACTION_MOVE:
                        float dx = event.getRawX() - initialTouchX;
                        float dy = event.getRawY() - initialTouchY;

                        // Inward swipe detection (swipe left from right edge)
                        if (dx < -swipeThreshold && Math.abs(dx) > Math.abs(dy) && !isDraggingHandle && !isRepositioning) {
                            mainHandler.removeCallbacks(longPressRunnable);
                            expandPanel();
                            return true;
                        }

                        // Dragging detection
                        if ((Math.abs(dy) > touchSlop && Math.abs(dy) > Math.abs(dx)) || isRepositioning) {
                            mainHandler.removeCallbacks(longPressRunnable);
                            isDraggingHandle = true;

                            currentHandleY = clampHandleY((int) (initialHandleY + dy));
                            if (windowParams != null) {
                                windowParams.y = currentHandleY;
                                try {
                                    windowManager.updateViewLayout(EdgeVolumeOverlayView.this, windowParams);
                                } catch (Throwable ignored) {}
                            }
                            return true;
                        }
                        return true;

                    case MotionEvent.ACTION_UP:
                    case MotionEvent.ACTION_CANCEL:
                        mainHandler.removeCallbacks(longPressRunnable);

                        long duration = System.currentTimeMillis() - touchDownTime;
                        float finalDx = Math.abs(event.getRawX() - initialTouchX);
                        float finalDy = Math.abs(event.getRawY() - initialTouchY);

                        if (isRepositioning || isDraggingHandle) {
                            exitRepositionMode();
                            // Persist new position ratio
                            float newRatio = (float) (currentHandleY + handleTouchHeight / 2) / (float) screenHeight;
                            EdgeVolumePreferences.setHandleYRatio(getContext(), newRatio);
                        } else if (finalDx < touchSlop && finalDy < touchSlop && duration < 450) {
                            // Tap detected -> expand panel
                            expandPanel();
                        } else {
                            updatePillOpacity();
                        }

                        isDraggingHandle = false;
                        isRepositioning = false;
                        return true;
                }
                return false;
            }
        });
    }

    private void enterRepositionMode() {
        isRepositioning = true;
        isDraggingHandle = true;
        vHandlePill.setBackgroundResource(R.drawable.bg_volume_handle_active);
        vHandlePill.setAlpha(0.95f);
        try {
            vHandlePill.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);
        } catch (Throwable ignored) {}
    }

    private void exitRepositionMode() {
        isRepositioning = false;
        vHandlePill.setBackgroundResource(R.drawable.bg_volume_handle);
        updatePillOpacity();
    }

    // =========================================================================
    // Expanded Volume Panel Management
    // =========================================================================

    public void expandPanel() {
        if (isExpanded) return;
        isExpanded = true;
        mainHandler.removeCallbacks(autoCollapseRunnable);

        // Switch WindowManager to full scrim window
        updateWindowToExpanded();

        // Position panel near handle vertically
        int panelHeightEstimate = dpToPx(240);
        int targetPanelY = currentHandleY + handleTouchHeight / 2 - panelHeightEstimate / 2;
        int minY = dpToPx(48);
        int maxY = screenHeight - panelHeightEstimate - dpToPx(48);
        int clampedPanelY = Math.max(minY, Math.min(maxY, targetPanelY));

        FrameLayout.LayoutParams lp = (FrameLayout.LayoutParams) llVolumePanel.getLayoutParams();
        lp.gravity = Gravity.END | Gravity.TOP;
        lp.topMargin = clampedPanelY;
        llVolumePanel.setLayoutParams(lp);

        // Sync real values
        syncVolumeViews();

        flHandleContainer.setVisibility(View.GONE);
        viewScrim.setVisibility(View.VISIBLE);
        llVolumePanel.setVisibility(View.VISIBLE);

        // Smooth slide-in animation from right edge
        llVolumePanel.setTranslationX(dpToPx(260));
        llVolumePanel.setAlpha(0.4f);
        llVolumePanel.animate()
                .translationX(0f)
                .alpha(1.0f)
                .setDuration(160)
                .setListener(null)
                .start();

        scheduleAutoCollapse();
    }

    public void collapsePanel() {
        if (!isExpanded) return;
        mainHandler.removeCallbacks(autoCollapseRunnable);

        llVolumePanel.animate()
                .translationX(dpToPx(260))
                .alpha(0.3f)
                .setDuration(140)
                .setListener(new AnimatorListenerAdapter() {
                    @Override
                    public void onAnimationEnd(Animator animation) {
                        llVolumePanel.setVisibility(View.GONE);
                        viewScrim.setVisibility(View.GONE);
                        flHandleContainer.setVisibility(View.VISIBLE);
                        isExpanded = false;
                        updateWindowToCollapsed();
                        updatePillOpacity();
                    }
                })
                .start();
    }

    private void scheduleAutoCollapse() {
        mainHandler.removeCallbacks(autoCollapseRunnable);
        if (!isUserDraggingSlider && isExpanded) {
            mainHandler.postDelayed(autoCollapseRunnable, AUTO_COLLAPSE_DELAY_MS);
        }
    }

    // =========================================================================
    // SeekBar and Volume Synchronization
    // =========================================================================

    private void setupSeekBars() {
        // Media Stream
        initStreamSeekBar(seekMedia, AudioManager.STREAM_MUSIC, tvMediaValue, new Runnable() {
            @Override
            public void run() {
                updateMediaMuteButtonState();
            }
        });

        // Ring Stream
        initStreamSeekBar(seekRing, AudioManager.STREAM_RING, tvRingValue, null);

        // Alarm Stream
        initStreamSeekBar(seekAlarm, AudioManager.STREAM_ALARM, tvAlarmValue, null);
    }

    private void initStreamSeekBar(final SeekBar seekBar, final int streamType, final TextView valueView, final Runnable extraOnChanged) {
        int min = volumeController.getMinVolume(streamType);
        int max = volumeController.getMaxVolume(streamType);
        int range = Math.max(1, max - min);

        seekBar.setMax(range);
        int current = volumeController.getVolume(streamType);
        seekBar.setProgress(Math.max(0, current - min));
        updateValueDisplay(valueView, streamType);

        seekBar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override
            public void onProgressChanged(SeekBar sb, int progress, boolean fromUser) {
                if (fromUser) {
                    int targetVolume = volumeController.getMinVolume(streamType) + progress;
                    volumeController.setVolume(streamType, targetVolume);
                    int pct = (range > 0) ? Math.round(((float) progress / (float) range) * 100f) : 0;
                    if (valueView != null) {
                        valueView.setText(String.format(Locale.US, "%d%%", pct));
                    }
                    if (extraOnChanged != null) extraOnChanged.run();
                    scheduleAutoCollapse();
                }
            }

            @Override
            public void onStartTrackingTouch(SeekBar sb) {
                isUserDraggingSlider = true;
                mainHandler.removeCallbacks(autoCollapseRunnable);
            }

            @Override
            public void onStopTrackingTouch(SeekBar sb) {
                isUserDraggingSlider = false;
                scheduleAutoCollapse();
            }
        });
    }

    private void updateValueDisplay(TextView valueView, int streamType) {
        if (valueView == null) return;
        int pct = volumeController.getVolumePercentage(streamType);
        valueView.setText(String.format(Locale.US, "%d%%", pct));
    }

    private void updateMediaMuteButtonState() {
        boolean isMuted = volumeController.isMediaMuted();
        if (isMuted) {
            btnMediaMute.setImageResource(R.drawable.ic_volume_mute);
            btnMediaMute.setColorFilter(getResources().getColor(R.color.status_warning));
            ivMediaIcon.setColorFilter(getResources().getColor(R.color.status_warning));
        } else {
            btnMediaMute.setImageResource(R.drawable.ic_volume_media);
            btnMediaMute.setColorFilter(getResources().getColor(R.color.text_primary));
            ivMediaIcon.setColorFilter(getResources().getColor(R.color.text_primary));
        }
    }

    /**
     * Synchronizes all seekbars and percentage labels with current device volume.
     * Called when panel opens or when system broadcasts VOLUME_CHANGED_ACTION.
     */
    public void syncVolumeViews() {
        if (!isUserDraggingSlider) {
            syncSingleStream(seekMedia, AudioManager.STREAM_MUSIC, tvMediaValue);
            syncSingleStream(seekRing, AudioManager.STREAM_RING, tvRingValue);
            syncSingleStream(seekAlarm, AudioManager.STREAM_ALARM, tvAlarmValue);
            updateMediaMuteButtonState();
        }
    }

    private void syncSingleStream(SeekBar seekBar, int streamType, TextView valueView) {
        if (seekBar == null) return;
        int min = volumeController.getMinVolume(streamType);
        int max = volumeController.getMaxVolume(streamType);
        int range = Math.max(1, max - min);
        int current = volumeController.getVolume(streamType);

        seekBar.setMax(range);
        seekBar.setProgress(Math.max(0, current - min));
        updateValueDisplay(valueView, streamType);
    }

    public void onExternalVolumeChanged() {
        post(new Runnable() {
            @Override
            public void run() {
                if (isExpanded) {
                    syncVolumeViews();
                }
            }
        });
    }

    @Override
    protected void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        updateScreenMetrics();
        float savedRatio = EdgeVolumePreferences.getHandleYRatio(getContext());
        currentHandleY = clampHandleY((int) (screenHeight * savedRatio) - handleTouchHeight / 2);

        if (!isExpanded && windowParams != null) {
            windowParams.y = currentHandleY;
            try {
                windowManager.updateViewLayout(this, windowParams);
            } catch (Throwable ignored) {}
        }
    }

    public void cleanup() {
        mainHandler.removeCallbacksAndMessages(null);
    }
}
