package com.rmx.cachecleaner;

import android.accessibilityservice.AccessibilityService;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;

import java.util.ArrayList;

/**
 * Dedicated synchronous IPC ContentProvider running in process :controls.
 *
 * Serves as the single authoritative owner of:
 * 1. Edge Volume Control state, preferences, and service lifecycle.
 * 2. Accessibility Service (Power Menu trigger and connection state).
 * 3. Settings UI Inspection / Pilot batch execution dispatch.
 *
 * Uses standard ContentProvider.call() for zero-database, synchronous, low-overhead IPC.
 */
public class ControlsProvider extends ContentProvider {

    private static final String TAG = "ControlsProvider";
    public static final String AUTHORITY = "com.rmx.cachecleaner.controls";
    public static final Uri CONTENT_URI = Uri.parse("content://" + AUTHORITY);

    // IPC Method Constants
    public static final String METHOD_GET_CONTROLS_STATE = "getControlsState";
    public static final String METHOD_ENABLE_EDGE_VOLUME = "enableEdgeVolume";
    public static final String METHOD_SET_HANDLE_OPACITY = "setHandleOpacity";
    public static final String METHOD_RESET_HANDLE_POSITION = "resetHandlePosition";
    public static final String METHOD_TRIGGER_POWER_MENU = "triggerPowerMenu";
    public static final String METHOD_IS_A11Y_CONNECTED = "isA11yConnected";
    public static final String METHOD_START_PILOT = "startPilot";
    public static final String METHOD_START_BATCH = "startBatch";
    public static final String METHOD_CANCEL_BATCH = "cancelBatch";
    public static final String METHOD_CONFIRM_PILOT_STEP2 = "confirmPilotStep2";
    public static final String METHOD_IS_BATCH_RUNNING = "isBatchRunning";

    // Bundle Key Constants
    public static final String KEY_IS_EDGE_VOLUME_ENABLED = "is_edge_volume_enabled";
    public static final String KEY_IS_EDGE_VOLUME_RUNNING = "is_edge_volume_running";
    public static final String KEY_HANDLE_OPACITY = "handle_opacity";
    public static final String KEY_HANDLE_Y_RATIO = "handle_y_ratio";
    public static final String KEY_IS_A11Y_CONNECTED = "is_a11y_connected";
    public static final String KEY_SUCCESS = "success";
    public static final String KEY_STARTED = "started";
    public static final String KEY_PACKAGES = "packages";
    public static final String KEY_IS_BATCH_RUNNING = "is_batch_running";
    public static final String KEY_BATCH_SUMMARY = "batch_summary";

    @Override
    public boolean onCreate() {
        Log.i(TAG, "ControlsProvider initialized in process :controls");
        return true;
    }

    @Override
    public Bundle call(String method, String arg, Bundle extras) {
        Context context = getContext();
        if (context == null) return null;

        Bundle response = new Bundle();

        switch (method) {
            case METHOD_GET_CONTROLS_STATE:
                response.putBoolean(KEY_IS_EDGE_VOLUME_ENABLED, EdgeVolumePreferences.isEnabled(context));
                response.putBoolean(KEY_IS_EDGE_VOLUME_RUNNING, EdgeVolumeService.isServiceRunning());
                response.putFloat(KEY_HANDLE_OPACITY, EdgeVolumePreferences.getHandleOpacity(context));
                response.putFloat(KEY_HANDLE_Y_RATIO, EdgeVolumePreferences.getHandleYRatio(context));
                response.putBoolean(KEY_IS_A11Y_CONNECTED, isAccessibilityRunning());
                response.putBoolean(KEY_IS_BATCH_RUNNING, PilotController.getInstance().isBatchRunning());
                response.putString(KEY_BATCH_SUMMARY, PilotController.getInstance().getLastBatchSummary());
                return response;

            case METHOD_ENABLE_EDGE_VOLUME:
                boolean enable = Boolean.parseBoolean(arg);
                EdgeVolumePreferences.setEnabled(context, enable);
                if (enable) {
                    EdgeVolumeService.start(context);
                } else {
                    EdgeVolumeService.stop(context);
                }
                response.putBoolean(KEY_SUCCESS, true);
                return response;

            case METHOD_SET_HANDLE_OPACITY:
                try {
                    float opacity = Float.parseFloat(arg);
                    EdgeVolumePreferences.setHandleOpacity(context, opacity);
                    EdgeVolumeService.updateOpacity(context);
                    response.putBoolean(KEY_SUCCESS, true);
                } catch (Throwable t) {
                    Log.w(TAG, "Invalid opacity: " + arg);
                    response.putBoolean(KEY_SUCCESS, false);
                }
                return response;

            case METHOD_RESET_HANDLE_POSITION:
                EdgeVolumePreferences.resetPosition(context);
                EdgeVolumeService.resetPosition(context);
                response.putBoolean(KEY_SUCCESS, true);
                return response;

            case METHOD_IS_A11Y_CONNECTED:
                response.putBoolean(KEY_IS_A11Y_CONNECTED, isAccessibilityRunning());
                return response;

            case METHOD_TRIGGER_POWER_MENU:
                SettingsInspectionAccessibilityService a11y = SettingsInspectionAccessibilityService.getInstance();
                boolean powerSuccess = false;
                if (a11y != null) {
                    powerSuccess = a11y.performGlobalAction(AccessibilityService.GLOBAL_ACTION_POWER_DIALOG);
                }
                response.putBoolean(KEY_SUCCESS, powerSuccess);
                return response;

            case METHOD_START_PILOT:
                boolean pilotStarted = PilotController.getInstance().startPilot(context);
                response.putBoolean(KEY_STARTED, pilotStarted);
                return response;

            case METHOD_START_BATCH:
                ArrayList<String> packages = (extras != null) ? extras.getStringArrayList(KEY_PACKAGES) : null;
                boolean batchStarted = PilotController.getInstance().startBatchFromPackages(context, packages);
                response.putBoolean(KEY_STARTED, batchStarted);
                return response;

            case METHOD_CANCEL_BATCH:
                String reason = (arg != null) ? arg : "Cancelled via IPC";
                PilotController.getInstance().cancel(reason);
                response.putBoolean(KEY_SUCCESS, true);
                return response;

            case METHOD_CONFIRM_PILOT_STEP2:
                PilotController.getInstance().confirmFinalExecution();
                response.putBoolean(KEY_SUCCESS, true);
                return response;

            case METHOD_IS_BATCH_RUNNING:
                response.putBoolean(KEY_IS_BATCH_RUNNING, PilotController.getInstance().isBatchRunning());
                response.putString(KEY_BATCH_SUMMARY, PilotController.getInstance().getLastBatchSummary());
                return response;

            default:
                Log.w(TAG, "Unknown IPC method: " + method);
                return super.call(method, arg, extras);
        }
    }

    private boolean isAccessibilityRunning() {
        return SettingsInspectionAccessibilityService.isServiceRunning()
                && SettingsInspectionAccessibilityService.getInstance() != null;
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        return null;
    }

    @Override
    public String getType(Uri uri) {
        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        return null;
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        return 0;
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        return 0;
    }
}
