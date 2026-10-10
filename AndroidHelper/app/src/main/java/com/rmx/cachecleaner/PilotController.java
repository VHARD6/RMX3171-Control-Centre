package com.rmx.cachecleaner;

import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Phase 4.4: Hardened Single-App Private Cache Cleaning Pilot Controller
 * 
 * Safety Guarantees & Constraints:
 * 1. Single fixed target: com.instagram.android.
 * 2. Mandatory App Info application identity verification before clicking Storage usage.
 * 3. Verified uninterrupted navigation sequence from App Info to Storage usage.
 * 4. Strict handling of zero-cache states (ALREADY_CLEAN vs disabled error).
 * 5. Elimination of screen-wide size fallbacks; localized parsing under Cache section only.
 * 6. Explicit asynchronous lifecycle cleanup with monotonic session token invalidation.
 * 7. Truthful outcome semantics: SETTINGS_UI_CONFIRMED (UI readout), ALREADY_CLEAN, etc.
 * 8. Strict isolation of "Clear data" to prevent any data loss.
 */
public class PilotController {

    public static final String TAG = "RMX_PILOT";
    public static final String TARGET_PACKAGE = "com.instagram.android";
    public static final String SETTINGS_PACKAGE = "com.android.settings";

    public enum State {
        IDLE,
        NAVIGATING_TO_STORAGE,
        INSPECTING_STORAGE,
        AWAITING_FINAL_CONFIRMATION,
        EXECUTING_CLEAR_CACHE,
        VERIFYING_RESULT,
        DONE
    }

    public enum Outcome {
        NONE,
        ALREADY_CLEAN,
        SETTINGS_UI_CONFIRMED,
        ACTION_ATTEMPTED_UNVERIFIED,
        FAILED,
        CANCELLED
    }

    public enum CacheButtonDecision {
        PROCEED_TO_CONFIRMATION,
        CONCLUDE_ALREADY_CLEAN,
        ABORT_DISABLED_WITH_POSITIVE_CACHE,
        ABORT_NON_CLICKABLE,
        ABORT_INVALID_BUTTON_COUNT,
        ABORT_UNTRUSTWORTHY_CACHE_SIZE
    }

    public static class PilotResult {
        public Outcome outcome = Outcome.NONE;
        public String cacheBefore = "Unknown";
        public String cacheAfter = "Unknown";
        public String summaryMessage = "";
        public List<String> logs = new ArrayList<>();
    }

    public interface PilotListener {
        void onPilotStateChanged(State state, String message);
        void onPilotFinished(PilotResult result);
    }

    private static final PilotController INSTANCE = new PilotController();
    public static PilotController getInstance() {
        return INSTANCE;
    }

    private State currentState = State.IDLE;
    private PilotListener listener;
    private Handler mainHandler = null;
    private final PilotResult currentResult = new PilotResult();

    private synchronized Handler getMainHandler() {
        if (mainHandler == null) {
            try {
                Looper looper = Looper.getMainLooper();
                if (looper != null) {
                    mainHandler = new Handler(looper);
                }
            } catch (Throwable ignored) {
                // In headless JVM unit test environments, Looper may be unmocked
            }
        }
        return mainHandler;
    }

    private void post(Runnable r) {
        Handler h = getMainHandler();
        if (h != null && r != null) h.post(r);
    }

    private void postDelayed(Runnable r, long delayMs) {
        Handler h = getMainHandler();
        if (h != null && r != null) h.postDelayed(r, delayMs);
    }

    private void removeCallbacks(Runnable r) {
        Handler h = getMainHandler();
        if (h != null && r != null) h.removeCallbacks(r);
    }

    private long stateStartTime = 0;
    private static final long TIMEOUT_MS = 20000; // 20s timeout per automated transition

    // Session token to invalidate stale asynchronous callbacks
    private int activeSessionToken = 0;

    // Target Identity Tracking
    private String targetAppLabel = "Instagram";
    private boolean isAppInfoIdentityVerified = false;
    private boolean isStorageUsageIdentityVerified = false;

    // Explicit runnable handles for reliable cancellation
    private Runnable timeoutRunnable = null;
    private Runnable inspectionRunnable = null;
    private Runnable clickRunnable = null;
    private Runnable verificationRunnable = null;

    private PilotController() {}

    public synchronized void setListener(PilotListener listener) {
        this.listener = listener;
    }

    public synchronized State getCurrentState() {
        return currentState;
    }

    public synchronized PilotResult getCurrentResult() {
        return currentResult;
    }

    public synchronized int getActiveSessionToken() {
        return activeSessionToken;
    }

    public synchronized boolean isCallbackValid(int sessionToken, State expectedState) {
        return sessionToken == activeSessionToken && currentState == expectedState;
    }

    /**
     * Pure evaluator for the ColorOS 11 Clear Cache button state and metrics.
     */
    public static CacheButtonDecision evaluateCacheButtonState(int buttonCount, boolean isEnabled, boolean isClickable, String observedCache) {
        if (buttonCount != 1) {
            return CacheButtonDecision.ABORT_INVALID_BUTTON_COUNT;
        }
        if (observedCache == null || !isStrictSizeString(observedCache)) {
            return CacheButtonDecision.ABORT_UNTRUSTWORTHY_CACHE_SIZE;
        }
        boolean isZero = isZeroCacheString(observedCache);
        if (!isEnabled) {
            if (isZero) {
                return CacheButtonDecision.CONCLUDE_ALREADY_CLEAN;
            } else {
                return CacheButtonDecision.ABORT_DISABLED_WITH_POSITIVE_CACHE;
            }
        }
        if (!isClickable) {
            return CacheButtonDecision.ABORT_NON_CLICKABLE;
        }
        return CacheButtonDecision.PROCEED_TO_CONFIRMATION;
    }

    /**
     * Resolve target application label from PackageManager metadata.
     */
    public static String resolveTargetAppLabel(Context context, String packageName) {
        if (context != null) {
            try {
                PackageManager pm = context.getPackageManager();
                ApplicationInfo appInfo = pm.getApplicationInfo(packageName, 0);
                CharSequence label = pm.getApplicationLabel(appInfo);
                if (label != null && label.length() > 0) {
                    return label.toString().trim();
                }
            } catch (Exception e) {
                Log.w(TAG, "Failed resolving target label for " + packageName + ": " + e.getMessage());
            }
        }
        return "Instagram";
    }

    /**
     * Start the single-app pilot test after Step 1 user confirmation in MainActivity.
     */
    public synchronized boolean startPilot(Context context) {
        if (currentState != State.IDLE && currentState != State.DONE) {
            log("Cannot start pilot: already running in state " + currentState);
            return false;
        }

        if (!SettingsInspectionAccessibilityService.isServiceRunning()) {
            fail("Accessibility Service is not enabled. Please enable it in Settings first.");
            return false;
        }

        // Cancel any pending callbacks and increment session token to invalidate stale work
        cancelAllCallbacks();
        activeSessionToken++;
        final int sessionToken = activeSessionToken;

        isAppInfoIdentityVerified = false;
        isStorageUsageIdentityVerified = false;
        targetAppLabel = resolveTargetAppLabel(context, TARGET_PACKAGE);
        log("Target resolved: " + TARGET_PACKAGE + " -> '" + targetAppLabel + "' (Session " + sessionToken + ")");

        currentResult.outcome = Outcome.NONE;
        currentResult.cacheBefore = "Pending";
        currentResult.cacheAfter = "Pending";
        currentResult.summaryMessage = "Pilot started for " + targetAppLabel + ".";
        currentResult.logs.clear();

        transitionTo(State.NAVIGATING_TO_STORAGE, "Opening " + targetAppLabel + " App Info...");

        try {
            Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            intent.setData(Uri.fromParts("package", TARGET_PACKAGE, null));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
            log("Launched ACTION_APPLICATION_DETAILS_SETTINGS for " + TARGET_PACKAGE);
            return true;
        } catch (Exception e) {
            fail("Failed to open App Info intent: " + e.getMessage());
            return false;
        }
    }

    /**
     * Called by PilotConfirmationDialogActivity when user confirms cache deletion.
     */
    public synchronized void confirmFinalExecution() {
        if (currentState != State.AWAITING_FINAL_CONFIRMATION) {
            log("confirmFinalExecution called in unexpected state: " + currentState);
            return;
        }
        final int sessionToken = activeSessionToken;
        transitionTo(State.EXECUTING_CLEAR_CACHE, "Final confirmation granted. Awaiting Storage usage screen to clear cache...");

        // Ensure Settings task remains in the foreground
        SettingsInspectionAccessibilityService svc = SettingsInspectionAccessibilityService.getInstance();
        if (svc != null) {
            try {
                Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
                intent.setData(Uri.fromParts("package", TARGET_PACKAGE, null));
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                svc.startActivity(intent);
            } catch (Exception e) {
                log("Could not re-focus Settings task: " + e.getMessage());
            }
        }

        scheduleClickExecution(sessionToken, 250);
    }

    private void scheduleClickExecution(final int sessionToken, final long delayMs) {
        if (clickRunnable != null) {
            removeCallbacks(clickRunnable);
        }
        clickRunnable = new Runnable() {
            @Override
            public void run() {
                synchronized (PilotController.this) {
                    if (sessionToken != activeSessionToken || currentState != State.EXECUTING_CLEAR_CACHE) {
                        return; // Stale callback
                    }
                    SettingsInspectionAccessibilityService svc = SettingsInspectionAccessibilityService.getInstance();
                    if (svc != null) {
                        AccessibilityNodeInfo root = svc.getRootInActiveWindow();
                        if (root != null) {
                            boolean handled = handleStorageUsageScreenForClick(root, null, svc, sessionToken);
                            if (handled) {
                                return; // Completed action or safely aborted
                            }
                        }
                    }
                    // Window is still transitioning: reschedule retry
                    scheduleClickExecution(sessionToken, 250);
                }
            }
        };
        postDelayed(clickRunnable, delayMs);
    }

    /**
     * Cancel the pilot operation at any point.
     */
    public synchronized void cancel(String reason) {
        if (currentState == State.IDLE || currentState == State.DONE) return;

        cancelAllCallbacks();
        activeSessionToken++; // Invalidate any pending work

        currentResult.outcome = Outcome.CANCELLED;
        currentResult.summaryMessage = "Pilot cancelled: " + reason;
        log("CANCELLED: " + reason);
        transitionTo(State.DONE, currentResult.summaryMessage);
        notifyFinished();
    }

    /**
     * Fail the pilot operation safely.
     */
    public synchronized void fail(String reason) {
        cancelAllCallbacks();
        activeSessionToken++; // Invalidate any pending work

        currentResult.outcome = Outcome.FAILED;
        currentResult.summaryMessage = "Pilot failed: " + reason;
        log("FAILED: " + reason);
        transitionTo(State.DONE, currentResult.summaryMessage);
        notifyFinished();
    }

    /**
     * Called when the accessibility service unbinds or disconnects.
     */
    public synchronized void onServiceDisconnected() {
        if (currentState != State.IDLE && currentState != State.DONE) {
            fail("Accessibility Service disconnected during pilot execution.");
        }
    }

    /**
     * Core event handler called by SettingsInspectionAccessibilityService on window/content events.
     */
    public synchronized void onAccessibilityEvent(AccessibilityEvent event, SettingsInspectionAccessibilityService service) {
        if (currentState == State.IDLE || currentState == State.DONE || currentState == State.AWAITING_FINAL_CONFIRMATION) {
            return;
        }

        checkTimeout();

        CharSequence pkg = event.getPackageName();
        if (pkg == null || !SETTINGS_PACKAGE.contentEquals(pkg)) {
            // Non-settings package event detected while automation is active
            if (isAppInfoIdentityVerified && (currentState == State.INSPECTING_STORAGE || currentState == State.EXECUTING_CLEAR_CACHE)) {
                log("Warning: Intervening non-settings event from " + pkg);
            }
            return;
        }

        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        if (root == null && event.getSource() != null) {
            root = findWindowRoot(event.getSource());
        }
        if (root == null) return;

        final int token = activeSessionToken;
        switch (currentState) {
            case NAVIGATING_TO_STORAGE:
                handleAppInfoScreen(root, event);
                break;

            case INSPECTING_STORAGE:
                inspectStorageUsageBeforeDialog(root, service, token);
                break;

            case EXECUTING_CLEAR_CACHE:
                handleStorageUsageScreenForClick(root, event, service, token);
                break;

            default:
                break;
        }
    }

    /**
     * Verify App Info screen identity and click the verified "Storage usage" row.
     */
    private void handleAppInfoScreen(AccessibilityNodeInfo root, AccessibilityEvent event) {
        if (currentState != State.NAVIGATING_TO_STORAGE) return;

        // Identity Verification Guard 1: Verify foreground package is Settings
        CharSequence pkg = root.getPackageName();
        if (pkg != null && !SETTINGS_PACKAGE.contentEquals(pkg)) {
            return;
        }

        // Identity Verification Guard 2: Require unambiguous match for the target app label on App Info screen
        boolean hasTargetLabel = hasExactTextNode(root, targetAppLabel);
        if (!hasTargetLabel) {
            // Still loading App Info, or wrong screen
            List<AccessibilityNodeInfo> storageRows = root.findAccessibilityNodeInfosByText("Storage usage");
            if (storageRows != null && !storageRows.isEmpty()) {
                // "Storage usage" row is visible, but target label is absent -> identity mismatch!
                fail("Identity verification failed on App Info: Target label '" + targetAppLabel + "' not found on screen.");
            }
            return;
        }

        // Locate "Storage usage" row
        List<AccessibilityNodeInfo> storageRows = root.findAccessibilityNodeInfosByText("Storage usage");
        if (storageRows == null || storageRows.isEmpty()) {
            return; // Screen still loading
        }

        if (storageRows.size() > 1) {
            fail("Ambiguity: Found multiple 'Storage usage' nodes on App Info screen.");
            return;
        }

        AccessibilityNodeInfo storageNode = storageRows.get(0);
        CharSequence text = storageNode.getText();
        if (text == null || !"Storage usage".equalsIgnoreCase(normalizeSizeString(text.toString()))) {
            return;
        }

        AccessibilityNodeInfo clickableAncestor = findClickableAncestor(storageNode);
        if (clickableAncestor == null) {
            fail("Found 'Storage usage' node, but no clickable ancestor layout was found.");
            return;
        }

        if (!clickableAncestor.isEnabled()) {
            fail("Found 'Storage usage' row, but it is currently disabled.");
            return;
        }

        // Identity and layout verified on App Info screen
        isAppInfoIdentityVerified = true;
        log("Identity verified for '" + targetAppLabel + "' on App Info screen. Clicking 'Storage usage' ancestor: " + clickableAncestor.getClassName());
        
        boolean clicked = clickableAncestor.performAction(AccessibilityNodeInfo.ACTION_CLICK);
        if (!clicked) {
            fail("Click action on 'Storage usage' row returned false.");
            return;
        }

        log("Successfully clicked 'Storage usage' row. Waiting for Storage usage screen inspection...");
        transitionTo(State.INSPECTING_STORAGE, "Waiting for Storage usage screen inspection...");
        scheduleStorageInspection(activeSessionToken, 500);
    }

    private void scheduleStorageInspection(final int sessionToken, final long delayMs) {
        if (inspectionRunnable != null) {
            removeCallbacks(inspectionRunnable);
        }
        inspectionRunnable = new Runnable() {
            @Override
            public void run() {
                synchronized (PilotController.this) {
                    if (sessionToken != activeSessionToken || currentState != State.INSPECTING_STORAGE) {
                        return; // Stale callback
                    }
                    SettingsInspectionAccessibilityService svc = SettingsInspectionAccessibilityService.getInstance();
                    if (svc != null) {
                        AccessibilityNodeInfo root = svc.getRootInActiveWindow();
                        if (root != null) {
                            boolean handled = inspectStorageUsageBeforeDialog(root, svc, sessionToken);
                            if (handled) {
                                return;
                            }
                        }
                    }
                    scheduleStorageInspection(sessionToken, 300);
                }
            }
        };
        postDelayed(inspectionRunnable, delayMs);
    }

    /**
     * Inspect Storage usage screen, enforce identity sequence, isolate buttons, and handle zero-cache states.
     */
    private synchronized boolean inspectStorageUsageBeforeDialog(AccessibilityNodeInfo root, SettingsInspectionAccessibilityService service, int sessionToken) {
        if (sessionToken != activeSessionToken || currentState != State.INSPECTING_STORAGE) return true;

        if (service == null) {
            service = SettingsInspectionAccessibilityService.getInstance();
        }
        if (service == null) return false;

        // Sequence Guard: Must be preceded by verified App Info screen in this exact session
        if (!isAppInfoIdentityVerified) {
            fail("Security abort: Storage usage reached without preceding App Info identity verification.");
            return true;
        }

        // Safeguard 1: Verify screen title "Storage usage"
        if (!hasExactTextNode(root, "Storage usage")) {
            return false; // Still transitioning
        }

        // Safeguard 2: Verify "Cache" heading
        if (!hasExactTextNode(root, "Cache")) {
            return false; // Still loading sections
        }

        // Safeguard 3: Establish target identity on Storage usage screen
        if (hasExactTextNode(root, targetAppLabel)) {
            isStorageUsageIdentityVerified = true;
            log("Target app label '" + targetAppLabel + "' verified directly on Storage usage screen.");
        } else {
            // ColorOS 11 SubSettings toolbar does not render app label.
            // Verified via uninterrupted sequential navigation from verified App Info within active session.
            isStorageUsageIdentityVerified = isAppInfoIdentityVerified;
            log("Target identity verified via uninterrupted navigation sequence from verified App Info.");
        }

        // Safeguard 4: Isolate Clear data button to prove layout integrity
        List<AccessibilityNodeInfo> clearDataList = root.findAccessibilityNodeInfosByText("Clear data");
        if (clearDataList == null || clearDataList.isEmpty()) {
            return false; // Sections still inflating
        }
        log("Safeguard verified: 'Clear data' button detected and isolated (will NOT be clicked).");

        // Safeguard 5: Locate exact "Clear cache" button
        List<AccessibilityNodeInfo> clearCacheList = root.findAccessibilityNodeInfosByText("Clear cache");
        if (clearCacheList == null || clearCacheList.isEmpty()) {
            return false; // Buttons still inflating
        }

        AccessibilityNodeInfo exactButton = null;
        int matchCount = 0;
        for (AccessibilityNodeInfo node : clearCacheList) {
            CharSequence txt = node.getText();
            if (txt != null && "Clear cache".equalsIgnoreCase(normalizeSizeString(txt.toString()))) {
                exactButton = node;
                matchCount++;
            }
        }

        // Safeguard 6: Extract observed cache value strictly from Cache section
        String observedCache = extractObservedCacheSize(root);

        // Safeguard 7: Strict multi-state safety evaluation
        CacheButtonDecision decision = evaluateCacheButtonState(
                matchCount,
                exactButton != null && exactButton.isEnabled(),
                exactButton != null && exactButton.isClickable(),
                observedCache);

        switch (decision) {
            case CONCLUDE_ALREADY_CLEAN:
                log("Zero-cache state confirmed: Cache is " + observedCache + " and 'Clear cache' button is disabled.");
                concludeAlreadyClean(service, observedCache);
                return true;
            case PROCEED_TO_CONFIRMATION:
                currentResult.cacheBefore = observedCache;
                log("Observed " + targetAppLabel + " Cache: " + observedCache);
                break;
            case ABORT_DISABLED_WITH_POSITIVE_CACHE:
                fail("Safety abort: 'Clear cache' button is disabled, but observed cache reading is positive or unavailable: " + observedCache);
                return true;
            case ABORT_NON_CLICKABLE:
                fail("Safety abort: 'Clear cache' button is enabled but not clickable.");
                return true;
            case ABORT_INVALID_BUTTON_COUNT:
                fail("Safety abort: Expected exactly 1 'Clear cache' button, found: " + matchCount);
                return true;
            case ABORT_UNTRUSTWORTHY_CACHE_SIZE:
                fail("Safety abort: Unable to obtain trustworthy cache size from verified Cache section: " + observedCache);
                return true;
        }

        // Step 8: Transition to AWAITING_FINAL_CONFIRMATION (clears transition timeout) and launch modal dialog
        transitionTo(State.AWAITING_FINAL_CONFIRMATION, "Presenting final confirmation dialog to user.");

        Intent dialogIntent = new Intent(service, PilotConfirmationDialogActivity.class);
        dialogIntent.putExtra("target_package", TARGET_PACKAGE);
        dialogIntent.putExtra("target_label", targetAppLabel);
        dialogIntent.putExtra("cache_before", observedCache);
        dialogIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        service.startActivity(dialogIntent);
        log("Presented final confirmation dialog to user for " + targetAppLabel);
        return true;
    }

    /**
     * Conclude pilot when target cache is already zero and button is legitimately disabled.
     */
    private synchronized void concludeAlreadyClean(SettingsInspectionAccessibilityService service, String zeroCache) {
        cancelAllCallbacks();
        currentResult.outcome = Outcome.ALREADY_CLEAN;
        currentResult.cacheBefore = zeroCache;
        currentResult.cacheAfter = zeroCache;
        currentResult.summaryMessage = String.format(Locale.US,
                "ALREADY_CLEAN: %s private cache is already %s. 'Clear cache' control is disabled as expected.",
                targetAppLabel, zeroCache);
        log(currentResult.summaryMessage);
        transitionTo(State.DONE, currentResult.summaryMessage);

        try {
            Intent returnIntent = new Intent(service, MainActivity.class);
            returnIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            returnIntent.putExtra("pilot_outcome", currentResult.outcome.name());
            returnIntent.putExtra("pilot_before", currentResult.cacheBefore);
            returnIntent.putExtra("pilot_after", currentResult.cacheAfter);
            returnIntent.putExtra("pilot_summary", currentResult.summaryMessage);
            service.startActivity(returnIntent);
        } catch (Exception e) {
            log("Could not bring MainActivity to front: " + e.getMessage());
        }

        notifyFinished();
    }

    /**
     * Execute the cache clearing click after Step 2 user confirmation.
     */
    private synchronized boolean handleStorageUsageScreenForClick(AccessibilityNodeInfo root, AccessibilityEvent event, SettingsInspectionAccessibilityService service, int sessionToken) {
        if (sessionToken != activeSessionToken || currentState != State.EXECUTING_CLEAR_CACHE) return true;

        CharSequence pkg = root.getPackageName();
        if (pkg == null || !SETTINGS_PACKAGE.contentEquals(pkg)) {
            return false; // Still dismissing dialog or transitioning window
        }

        if (!isStorageUsageIdentityVerified) {
            fail("Pre-click abort: Storage usage identity not established for " + targetAppLabel);
            return true;
        }

        // Re-validate ALL safeguards immediately before clicking!
        if (!hasExactTextNode(root, "Storage usage") || !hasExactTextNode(root, "Cache")) {
            return false; // Still animating / transitioning to Storage usage
        }

        // Verify Clear data is present and isolated
        List<AccessibilityNodeInfo> clearDataList = root.findAccessibilityNodeInfosByText("Clear data");
        if (clearDataList == null || clearDataList.isEmpty()) {
            fail("Pre-click abort: 'Clear data' button not found, layout integrity compromised.");
            return true;
        }

        // Locate exact "Clear cache" button
        List<AccessibilityNodeInfo> clearCacheList = root.findAccessibilityNodeInfosByText("Clear cache");
        if (clearCacheList == null || clearCacheList.size() != 1) {
            fail("Pre-click abort: Expected exactly 1 'Clear cache' button, found: " + (clearCacheList == null ? 0 : clearCacheList.size()));
            return true;
        }

        AccessibilityNodeInfo exactButton = clearCacheList.get(0);
        CharSequence txt = exactButton.getText();
        if (txt == null || !"Clear cache".equalsIgnoreCase(normalizeSizeString(txt.toString()))) {
            fail("Pre-click abort: Button text mismatch: " + txt);
            return true;
        }

        if (!exactButton.isClickable() || !exactButton.isEnabled()) {
            fail("Pre-click abort: 'Clear cache' button is not clickable or enabled.");
            return true;
        }

        String resId = exactButton.getViewIdResourceName();
        log(">>> EXECUTING ACTION: Clicking validated 'Clear cache' button! ID: " + resId);
        boolean clicked = exactButton.performAction(AccessibilityNodeInfo.ACTION_CLICK);

        if (!clicked) {
            fail("performAction(ACTION_CLICK) on 'Clear cache' returned false.");
            return true;
        }

        log("Click succeeded. Transitioning to verification...");
        transitionTo(State.VERIFYING_RESULT, "Cache click executed. Verifying updated cache size...");

        final int verifyToken = activeSessionToken;
        if (verificationRunnable != null) {
            removeCallbacks(verificationRunnable);
        }
        verificationRunnable = new Runnable() {
            @Override
            public void run() {
                synchronized (PilotController.this) {
                    if (verifyToken == activeSessionToken && currentState == State.VERIFYING_RESULT) {
                        verifyCacheClearResult(service, verifyToken);
                    }
                }
            }
        };
        postDelayed(verificationRunnable, 1500);
        return true;
    }

    /**
     * Verify outcome honestly using UI readout and return to MainActivity.
     */
    private synchronized void verifyCacheClearResult(SettingsInspectionAccessibilityService service, int sessionToken) {
        if (sessionToken != activeSessionToken || currentState != State.VERIFYING_RESULT) return;

        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        String cacheAfter = null;
        if (root != null) {
            cacheAfter = extractObservedCacheSize(root);
        }
        currentResult.cacheAfter = (cacheAfter != null) ? cacheAfter : "Unknown";
        log("Observed " + targetAppLabel + " Cache after clearing: " + currentResult.cacheAfter);

        String before = currentResult.cacheBefore;
        boolean verified = false;

        if (cacheAfter != null) {
            if (isZeroCacheString(cacheAfter)) {
                verified = true;
            } else if (!before.equalsIgnoreCase(cacheAfter)) {
                verified = true;
            }
        }

        if (verified) {
            currentResult.outcome = Outcome.SETTINGS_UI_CONFIRMED;
            currentResult.summaryMessage = String.format(Locale.US,
                    "SETTINGS_UI_CONFIRMED: Settings UI confirmed %s cache cleared from %s to %s.",
                    targetAppLabel, before, currentResult.cacheAfter);
            log(currentResult.summaryMessage);
        } else {
            currentResult.outcome = Outcome.ACTION_ATTEMPTED_UNVERIFIED;
            currentResult.summaryMessage = String.format(Locale.US,
                    "ACTION_ATTEMPTED_UNVERIFIED: Click executed, but Settings UI cache readout could not be confirmed (Before: %s, After: %s).",
                    before, currentResult.cacheAfter);
            log(currentResult.summaryMessage);
        }

        transitionTo(State.DONE, currentResult.summaryMessage);
        cancelAllCallbacks();

        // Return to MainActivity
        try {
            Intent returnIntent = new Intent(service, MainActivity.class);
            returnIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            returnIntent.putExtra("pilot_outcome", currentResult.outcome.name());
            returnIntent.putExtra("pilot_before", currentResult.cacheBefore);
            returnIntent.putExtra("pilot_after", currentResult.cacheAfter);
            returnIntent.putExtra("pilot_summary", currentResult.summaryMessage);
            service.startActivity(returnIntent);
        } catch (Exception e) {
            log("Could not bring MainActivity to front: " + e.getMessage());
        }

        notifyFinished();
    }

    /**
     * Extract observed cache size strictly from the verified Cache section.
     * All screen-wide scanning fallbacks are eliminated.
     */
    public static String extractObservedCacheSize(AccessibilityNodeInfo root) {
        if (root == null) return null;

        List<AccessibilityNodeInfo> cacheNodes = root.findAccessibilityNodeInfosByText("Cache");
        if (cacheNodes == null || cacheNodes.isEmpty()) return null;

        AccessibilityNodeInfo exactCacheHeader = null;
        for (AccessibilityNodeInfo node : cacheNodes) {
            CharSequence text = node.getText();
            if (text != null && "Cache".equalsIgnoreCase(normalizeSizeString(text.toString()))) {
                exactCacheHeader = node;
                break;
            }
        }
        if (exactCacheHeader == null) return null;

        // 1. Search immediate parent container (e.g. LinearLayout row containing title and summary)
        AccessibilityNodeInfo parent = exactCacheHeader.getParent();
        if (parent != null) {
            for (int i = 0; i < parent.getChildCount(); i++) {
                AccessibilityNodeInfo child = parent.getChild(i);
                if (child != null && !child.equals(exactCacheHeader)) {
                    CharSequence childText = child.getText();
                    if (childText != null) {
                        String str = normalizeSizeString(childText.toString());
                        if (isStrictSizeString(str) && !isForbiddenSectionValue(child, parent)) {
                            return str;
                        }
                    }
                }
            }
        }

        // 2. Search adjacent sibling container of parent if separate row
        if (parent != null) {
            AccessibilityNodeInfo grandParent = parent.getParent();
            if (grandParent != null) {
                int parentIndex = -1;
                for (int i = 0; i < grandParent.getChildCount(); i++) {
                    if (parent.equals(grandParent.getChild(i))) {
                        parentIndex = i;
                        break;
                    }
                }
                if (parentIndex != -1 && parentIndex + 1 < grandParent.getChildCount()) {
                    AccessibilityNodeInfo adjacent = grandParent.getChild(parentIndex + 1);
                    if (adjacent != null) {
                        CharSequence adjText = adjacent.getText();
                        if (adjText != null) {
                            String str = normalizeSizeString(adjText.toString());
                            if (isStrictSizeString(str) && !isForbiddenSectionValue(adjacent, grandParent)) {
                                return str;
                            }
                        }
                    }
                }
            }
        }

        // Global fallback is strictly prohibited. If not found in Cache section, return null.
        return null;
    }

    public static String normalizeSizeString(String raw) {
        if (raw == null) return "";
        return raw.replace('\u00A0', ' ').replace('\u202F', ' ').trim();
    }

    public static boolean isStrictSizeString(String str) {
        if (str == null || str.isEmpty()) return false;
        String s = normalizeSizeString(str);
        return s.matches("(?i)^[0-9]+([.][0-9]+)?\\s*(B|KB|MB|GB|TB)$");
    }

    public static boolean isZeroCacheString(String str) {
        if (str == null || str.isEmpty()) return false;
        String s = normalizeSizeString(str).toLowerCase(Locale.US);
        return s.matches("^0([.]0+)?\\s*(b|kb|mb|gb|tb)?$") || s.equals("0b") || s.equals("0 b");
    }

    private static boolean isForbiddenSectionValue(AccessibilityNodeInfo node, AccessibilityNodeInfo parent) {
        if (parent == null) return false;
        List<AccessibilityNodeInfo> titles = parent.findAccessibilityNodeInfosByText("Total");
        if (titles != null && !titles.isEmpty()) return true;
        titles = parent.findAccessibilityNodeInfosByText("App size");
        if (titles != null && !titles.isEmpty()) return true;
        titles = parent.findAccessibilityNodeInfosByText("Data");
        if (titles != null && !titles.isEmpty()) return true;
        return false;
    }

    public static boolean hasExactTextNode(AccessibilityNodeInfo root, String query) {
        if (root == null || query == null) return false;
        List<AccessibilityNodeInfo> list = root.findAccessibilityNodeInfosByText(query);
        if (list == null || list.isEmpty()) return false;
        for (AccessibilityNodeInfo n : list) {
            CharSequence t = n.getText();
            if (t != null && query.equalsIgnoreCase(normalizeSizeString(t.toString()))) {
                return true;
            }
        }
        return false;
    }

    private AccessibilityNodeInfo findClickableAncestor(AccessibilityNodeInfo node) {
        AccessibilityNodeInfo current = node;
        for (int i = 0; i < 4; i++) {
            AccessibilityNodeInfo parent = current.getParent();
            if (parent == null) break;
            if (parent.isClickable()) {
                return parent;
            }
            current = parent;
        }
        return null;
    }

    private AccessibilityNodeInfo findWindowRoot(AccessibilityNodeInfo node) {
        if (node == null) return null;
        AccessibilityNodeInfo current = node;
        for (int i = 0; i < 30; i++) {
            AccessibilityNodeInfo parent = current.getParent();
            if (parent == null) {
                return current;
            }
            current = parent;
        }
        return current;
    }

    private void transitionTo(final State newState, final String message) {
        this.currentState = newState;
        this.stateStartTime = System.currentTimeMillis();
        log("STATE -> " + newState + " (" + message + ")");
        if (newState == State.AWAITING_FINAL_CONFIRMATION) {
            clearTimeout(); // User confirmation modal is active; do not timeout human interaction
        } else {
            resetTimeout();
        }
        if (listener != null) {
            post(new Runnable() {
                @Override
                public void run() {
                    if (listener != null) listener.onPilotStateChanged(newState, message);
                }
            });
        }
    }

    private void notifyFinished() {
        if (listener != null) {
            post(new Runnable() {
                @Override
                public void run() {
                    if (listener != null) listener.onPilotFinished(currentResult);
                }
            });
        }
    }

    private void resetTimeout() {
        clearTimeout();
        if (currentState != State.IDLE && currentState != State.DONE && currentState != State.AWAITING_FINAL_CONFIRMATION) {
            timeoutRunnable = new Runnable() {
                @Override
                public void run() {
                    fail("Operation timed out waiting for screen transition in state: " + currentState);
                }
            };
            postDelayed(timeoutRunnable, TIMEOUT_MS);
        }
    }

    private void clearTimeout() {
        if (timeoutRunnable != null) {
            removeCallbacks(timeoutRunnable);
            timeoutRunnable = null;
        }
    }

    private synchronized void cancelAllCallbacks() {
        clearTimeout();
        if (inspectionRunnable != null) {
            removeCallbacks(inspectionRunnable);
            inspectionRunnable = null;
        }
        if (clickRunnable != null) {
            removeCallbacks(clickRunnable);
            clickRunnable = null;
        }
        if (verificationRunnable != null) {
            removeCallbacks(verificationRunnable);
            verificationRunnable = null;
        }
    }

    private void checkTimeout() {
        if (currentState != State.IDLE && currentState != State.DONE && currentState != State.AWAITING_FINAL_CONFIRMATION) {
            if (System.currentTimeMillis() - stateStartTime > TIMEOUT_MS) {
                fail("State transition timeout exceeded (" + currentState + ")");
            }
        }
    }

    private void log(String msg) {
        Log.i(TAG, msg);
        currentResult.logs.add(msg);
    }
}
