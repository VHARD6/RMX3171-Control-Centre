package com.rmx.cachecleaner;

import android.content.Context;
import android.os.Bundle;
import android.util.Log;

import java.util.ArrayList;

/**
 * Lightweight client helper used by the UI process to send explicit synchronous commands
 * and queries to ControlsProvider in the persistent :controls process.
 *
 * Avoids stale cross-process SharedPreferences caching by querying the authoritative
 * controls process directly.
 */
public class ControlsClient {

    private static final String TAG = "ControlsClient";

    public static Bundle getControlsState(Context context) {
        if (context == null) return null;
        try {
            return context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_GET_CONTROLS_STATE,
                    null,
                    null
            );
        } catch (Throwable t) {
            Log.w(TAG, "getControlsState IPC failed: " + t.getMessage());
            return null;
        }
    }

    public static boolean isA11yConnected(Context context) {
        if (SettingsInspectionAccessibilityService.isServiceRunning()) {
            return true;
        }
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_IS_A11Y_CONNECTED,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_IS_A11Y_CONNECTED, false);
        } catch (Throwable t) {
            Log.w(TAG, "isA11yConnected IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean triggerPowerMenu(Context context) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_TRIGGER_POWER_MENU,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "triggerPowerMenu IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean enableEdgeVolume(Context context, boolean enable) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_ENABLE_EDGE_VOLUME,
                    String.valueOf(enable),
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "enableEdgeVolume IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean setHandleOpacity(Context context, float opacity) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_SET_HANDLE_OPACITY,
                    String.valueOf(opacity),
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "setHandleOpacity IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean resetHandlePosition(Context context) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_RESET_HANDLE_POSITION,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "resetHandlePosition IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean startPilot(Context context) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_START_PILOT,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_STARTED, false);
        } catch (Throwable t) {
            Log.w(TAG, "startPilot IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean startBatch(Context context, ArrayList<String> packageNames) {
        if (context == null || packageNames == null) return false;
        try {
            Bundle extras = new Bundle();
            extras.putStringArrayList(ControlsProvider.KEY_PACKAGES, packageNames);
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_START_BATCH,
                    null,
                    extras
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_STARTED, false);
        } catch (Throwable t) {
            Log.w(TAG, "startBatch IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean cancelBatch(Context context, String reason) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_CANCEL_BATCH,
                    reason,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "cancelBatch IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean confirmPilotStep2(Context context) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_CONFIRM_PILOT_STEP2,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_SUCCESS, false);
        } catch (Throwable t) {
            Log.w(TAG, "confirmPilotStep2 IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static boolean isBatchRunning(Context context) {
        if (context == null) return false;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_IS_BATCH_RUNNING,
                    null,
                    null
            );
            return res != null && res.getBoolean(ControlsProvider.KEY_IS_BATCH_RUNNING, false);
        } catch (Throwable t) {
            Log.w(TAG, "isBatchRunning IPC failed: " + t.getMessage());
            return false;
        }
    }

    public static String getLastBatchSummary(Context context) {
        if (context == null) return null;
        try {
            Bundle res = context.getContentResolver().call(
                    ControlsProvider.CONTENT_URI,
                    ControlsProvider.METHOD_IS_BATCH_RUNNING,
                    null,
                    null
            );
            return (res != null) ? res.getString(ControlsProvider.KEY_BATCH_SUMMARY) : null;
        } catch (Throwable t) {
            Log.w(TAG, "getLastBatchSummary IPC failed: " + t.getMessage());
            return null;
        }
    }
}
