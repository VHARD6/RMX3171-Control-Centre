package com.rmx.cachecleaner;

import android.accessibilityservice.AccessibilityService;
import android.content.Context;
import android.content.Intent;
import android.provider.Settings;
import android.util.Log;

/**
 * Controller for the on-screen Power Menu feature in RMX Device Hub.
 *
 * Provides a reliable, guarded software route to Android's native power dialog
 * (GLOBAL_ACTION_POWER_DIALOG) via the active Accessibility Service.
 *
 * Guarantees:
 * 1. Invokes only the native system dialog; never triggers direct shutdown, reboot, or locking.
 * 2. Checks both system settings enablement and live in-memory service connectivity.
 * 3. Enforces debounce/throttling to prevent accidental rapid repeated taps.
 * 4. Strictly prevents triggering while an automated Deep Clean batch is in progress.
 * 5. Provides testable delegate abstraction for JUnit verification.
 */
public class PowerMenuController {

    private static final String TAG = "PowerMenuController";
    public static final long THROTTLE_WINDOW_MS = 1500;

    private static long lastInvocationTime = 0;

    public enum Status {
        READY,
        SERVICE_DISABLED,
        SERVICE_DISCONNECTED
    }

    public enum Result {
        SUCCESS,
        FAILED_SYSTEM_REJECTED,
        SERVICE_NOT_ENABLED,
        SERVICE_NOT_CONNECTED,
        CLEANING_IN_PROGRESS,
        THROTTLED
    }

    /**
     * Interface for performing accessibility actions, enabling mock injection during unit tests.
     */
    public interface ActionDelegate {
        boolean isServiceConnected();
        boolean isEnabledInSettings(Context context);
        boolean isCleaningInProgress();
        boolean performPowerDialog();
    }

    private static Context applicationContext = null;

    private static final ActionDelegate DEFAULT_DELEGATE = new ActionDelegate() {
        @Override
        public boolean isServiceConnected() {
            if (SettingsInspectionAccessibilityService.isServiceRunning()
                    && SettingsInspectionAccessibilityService.getInstance() != null) {
                return true;
            }
            return ControlsClient.isA11yConnected(applicationContext);
        }

        @Override
        public boolean isEnabledInSettings(Context context) {
            if (context == null) return false;
            try {
                String prefString = Settings.Secure.getString(
                        context.getContentResolver(),
                        Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
                if (prefString != null) {
                    String targetComponent = context.getPackageName() + "/" + SettingsInspectionAccessibilityService.class.getName();
                    return prefString.contains(targetComponent)
                            || prefString.contains(SettingsInspectionAccessibilityService.class.getName());
                }
            } catch (Throwable t) {
                Log.w(TAG, "Error querying accessibility settings: " + t.getMessage());
            }
            return false;
        }

        @Override
        public boolean isCleaningInProgress() {
            PilotController pilot = PilotController.getInstance();
            return pilot.getCurrentState() != PilotController.State.IDLE
                    && pilot.getCurrentState() != PilotController.State.DONE;
        }

        @Override
        public boolean performPowerDialog() {
            SettingsInspectionAccessibilityService service = SettingsInspectionAccessibilityService.getInstance();
            if (service != null) {
                return service.performGlobalAction(AccessibilityService.GLOBAL_ACTION_POWER_DIALOG);
            }
            return ControlsClient.triggerPowerMenu(applicationContext);
        }
    };

    private static ActionDelegate currentDelegate = DEFAULT_DELEGATE;

    public static synchronized void setDelegateForTesting(ActionDelegate delegate) {
        currentDelegate = (delegate != null) ? delegate : DEFAULT_DELEGATE;
    }

    public static synchronized void resetThrottleForTesting() {
        lastInvocationTime = 0;
    }

    /**
     * Inspects whether the required Accessibility service is ready, disabled, or disconnected.
     */
    public static Status getStatus(Context context) {
        if (context != null) {
            applicationContext = context.getApplicationContext();
        }
        if (currentDelegate.isServiceConnected()) {
            return Status.READY;
        }
        if (currentDelegate.isEnabledInSettings(context)) {
            return Status.SERVICE_DISCONNECTED;
        }
        return Status.SERVICE_DISABLED;
    }

    /**
     * Executes the native Android power dialog invocation with strict safety guards.
     */
    public static synchronized Result triggerPowerMenu(Context context) {
        if (context != null) {
            applicationContext = context.getApplicationContext();
        }
        long now = System.currentTimeMillis();
        if (now - lastInvocationTime < THROTTLE_WINDOW_MS) {
            Log.w(TAG, "Power menu invocation throttled (< " + THROTTLE_WINDOW_MS + "ms).");
            return Result.THROTTLED;
        }

        if (currentDelegate.isCleaningInProgress()) {
            Log.w(TAG, "Power menu rejected: Deep Clean batch is currently active.");
            return Result.CLEANING_IN_PROGRESS;
        }

        if (!currentDelegate.isServiceConnected()) {
            boolean enabledInSettings = currentDelegate.isEnabledInSettings(context);
            if (enabledInSettings) {
                Log.w(TAG, "Power menu rejected: Service is enabled in Settings but disconnected.");
                return Result.SERVICE_NOT_CONNECTED;
            } else {
                Log.w(TAG, "Power menu rejected: Accessibility service is disabled in Settings.");
                return Result.SERVICE_NOT_ENABLED;
            }
        }

        lastInvocationTime = now;
        boolean success = currentDelegate.performPowerDialog();
        Log.i(TAG, "GLOBAL_ACTION_POWER_DIALOG invoked -> result=" + success);

        if (success) {
            return Result.SUCCESS;
        } else {
            return Result.FAILED_SYSTEM_REJECTED;
        }
    }

    /**
     * Opens Android's system Accessibility Settings page.
     */
    public static void openAccessibilitySettings(Context context) {
        if (context == null) return;
        try {
            Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
        } catch (Exception e) {
            Log.e(TAG, "Failed to open accessibility settings: " + e.getMessage());
        }
    }
}
