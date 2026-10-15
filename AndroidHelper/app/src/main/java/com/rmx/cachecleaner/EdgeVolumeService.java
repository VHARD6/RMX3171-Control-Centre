package com.rmx.cachecleaner;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;
import android.provider.Settings;
import android.util.Log;
import android.view.WindowManager;

/**
 * Lightweight Foreground Service hosting the Edge Volume Control handle.
 *
 * Enforces:
 * 1. Minimal low-importance foreground notification (Android 11 requirement).
 * 2. Strict permission verification (Settings.canDrawOverlays) before window attachment.
 * 3. Fixed compact overlay window with zero ViewGroups and zero XML layouts.
 * 4. Zero broadcast receivers or background polling loops.
 * 5. Clean teardown and window detachment on service destruction.
 */
public class EdgeVolumeService extends Service {

    private static final String TAG = "EdgeVolumeService";

    public static final String ACTION_START_SERVICE = "com.rmx.cachecleaner.action.START_EDGE_VOLUME";
    public static final String ACTION_STOP_SERVICE = "com.rmx.cachecleaner.action.STOP_EDGE_VOLUME";
    public static final String ACTION_RESET_POSITION = "com.rmx.cachecleaner.action.RESET_HANDLE_POSITION";
    public static final String ACTION_UPDATE_OPACITY = "com.rmx.cachecleaner.action.UPDATE_HANDLE_OPACITY";

    private static final String CHANNEL_ID = "rmx_edge_volume_channel";
    private static final int NOTIFICATION_ID = 2002;

    private static volatile boolean isRunning = false;

    private WindowManager windowManager;
    private EdgeVolumeOverlayView overlayView;

    public static boolean isServiceRunning() {
        return isRunning;
    }

    public static void start(Context context) {
        if (context == null) return;
        Intent intent = new Intent(context, EdgeVolumeService.class);
        intent.setAction(ACTION_START_SERVICE);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            context.startForegroundService(intent);
        } else {
            context.startService(intent);
        }
    }

    public static void stop(Context context) {
        if (context == null) return;
        Intent intent = new Intent(context, EdgeVolumeService.class);
        intent.setAction(ACTION_STOP_SERVICE);
        context.startService(intent);
    }

    public static void resetPosition(Context context) {
        if (context == null || !isRunning) return;
        Intent intent = new Intent(context, EdgeVolumeService.class);
        intent.setAction(ACTION_RESET_POSITION);
        context.startService(intent);
    }

    public static void updateOpacity(Context context) {
        if (context == null || !isRunning) return;
        Intent intent = new Intent(context, EdgeVolumeService.class);
        intent.setAction(ACTION_UPDATE_OPACITY);
        context.startService(intent);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        Log.i(TAG, "EdgeVolumeService onCreate");

        createNotificationChannel();
        Notification notification = buildNotification();
        startForeground(NOTIFICATION_ID, notification);

        // Verify overlay permission
        if (!Settings.canDrawOverlays(this)) {
            Log.w(TAG, "Overlay permission not granted; stopping EdgeVolumeService.");
            stopSelf();
            return;
        }

        windowManager = (WindowManager) getSystemService(Context.WINDOW_SERVICE);
        overlayView = new EdgeVolumeOverlayView(this, windowManager);

        try {
            windowManager.addView(overlayView, overlayView.getWindowParams());
            isRunning = true;
        } catch (Throwable t) {
            Log.e(TAG, "Failed to attach overlay window: " + t.getMessage(), t);
            stopSelf();
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String action = intent.getAction();
            if (ACTION_STOP_SERVICE.equals(action)) {
                Log.i(TAG, "Stop action requested via intent.");
                EdgeVolumePreferences.setEnabled(this, false);
                stopSelf();
                return START_NOT_STICKY;
            } else if (ACTION_RESET_POSITION.equals(action)) {
                if (overlayView != null) {
                    overlayView.resetHandlePosition();
                }
            } else if (ACTION_UPDATE_OPACITY.equals(action)) {
                if (overlayView != null) {
                    overlayView.updateOpacity();
                }
            }
        }
        return START_STICKY;
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Edge Volume Control",
                    NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("Shows discreet on-screen volume handle over other apps.");
            channel.setShowBadge(false);

            NotificationManager nm = getSystemService(NotificationManager.class);
            if (nm != null) {
                nm.createNotificationChannel(channel);
            }
        }
    }

    private Notification buildNotification() {
        Intent openAppIntent = new Intent(this, MainActivity.class);
        openAppIntent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pendingOpenIntent = PendingIntent.getActivity(
                this, 0, openAppIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M ? PendingIntent.FLAG_IMMUTABLE : 0)
        );

        Intent stopIntent = new Intent(this, EdgeVolumeService.class);
        stopIntent.setAction(ACTION_STOP_SERVICE);
        PendingIntent pendingStopIntent = PendingIntent.getService(
                this, 1, stopIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M ? PendingIntent.FLAG_IMMUTABLE : 0)
        );

        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }

        builder.setSmallIcon(R.drawable.ic_volume_media)
                .setContentTitle("Edge Volume Control Active")
                .setContentText("Pull out from the right screen edge to adjust volume.")
                .setContentIntent(pendingOpenIntent)
                .setOngoing(true)
                .addAction(R.drawable.ic_volume_close, "Stop", pendingStopIntent);

        return builder.build();
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        Log.i(TAG, "EdgeVolumeService onDestroy");
        isRunning = false;

        if (overlayView != null) {
            overlayView.cleanup();
            if (windowManager != null) {
                try {
                    windowManager.removeView(overlayView);
                } catch (Throwable t) {
                    Log.w(TAG, "Error removing overlay view: " + t.getMessage());
                }
            }
            overlayView = null;
        }
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
