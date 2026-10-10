package com.rmx.cachecleaner;

import android.content.Context;
import android.content.Intent;
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
 * Phase 4.2: Single-App Private Cache Cleaning Proof of Concept Controller
 * 
 * Strict Pilot Safety Rules:
 * 1. Single target only: com.instagram.android.
 * 2. Two explicit confirmation steps (MainActivity + Final Dialog).
 * 3. Multi-point validation of screen identity, title, and buttons before EVERY click.
 * 4. Isolates "Clear data" to prevent any accidental click.
 * 5. Bounded timeouts (20 seconds) on automated transitions; NO timeout while waiting for human confirmation.
 * 6. Explicit outcome reporting: SUCCESS_VERIFIED, ACTION_ATTEMPTED_UNVERIFIED, FAILED, CANCELLED.
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
        SUCCESS_VERIFIED,
        ACTION_ATTEMPTED_UNVERIFIED,
        FAILED,
        CANCELLED
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
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final PilotResult currentResult = new PilotResult();

    private long stateStartTime = 0;
    private static final long TIMEOUT_MS = 20000; // 20s timeout per automated transition

    private Runnable timeoutRunnable;

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

        currentResult.outcome = Outcome.NONE;
        currentResult.cacheBefore = "Pending";
        currentResult.cacheAfter = "Pending";
        currentResult.summaryMessage = "Pilot started for Instagram.";
        currentResult.logs.clear();

        transitionTo(State.NAVIGATING_TO_STORAGE, "Opening Instagram App Info...");

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
        transitionTo(State.EXECUTING_CLEAR_CACHE, "Final confirmation granted. Awaiting Storage usage screen to clear cache...");
        scheduleClickExecution(250);
    }

    private void scheduleClickExecution(final long delayMs) {
        mainHandler.postDelayed(new Runnable() {
            @Override
            public void run() {
                synchronized (PilotController.this) {
                    if (currentState == State.EXECUTING_CLEAR_CACHE) {
                        SettingsInspectionAccessibilityService svc = SettingsInspectionAccessibilityService.getInstance();
                        if (svc != null) {
                            AccessibilityNodeInfo root = svc.getRootInActiveWindow();
                            if (root != null) {
                                handleStorageUsageScreenForClick(root, null, svc);
                                return;
                            }
                        }
                        scheduleClickExecution(300);
                    }
                }
            }
        }, delayMs);
    }

    /**
     * Cancel the pilot operation at any point.
     */
    public synchronized void cancel(String reason) {
        if (currentState == State.IDLE || currentState == State.DONE) return;

        clearTimeout();
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
        clearTimeout();
        currentResult.outcome = Outcome.FAILED;
        currentResult.summaryMessage = "Pilot failed: " + reason;
        log("FAILED: " + reason);
        transitionTo(State.DONE, currentResult.summaryMessage);
        notifyFinished();
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
            return;
        }

        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        if (root == null && event.getSource() != null) {
            root = findWindowRoot(event.getSource());
        }
        if (root == null) return;

        switch (currentState) {
            case NAVIGATING_TO_STORAGE:
                handleAppInfoScreen(root, event);
                break;

            case INSPECTING_STORAGE:
                inspectStorageUsageBeforeDialog(root, service);
                break;

            case EXECUTING_CLEAR_CACHE:
                handleStorageUsageScreenForClick(root, event, service);
                break;

            default:
                break;
        }
    }

    /**
     * Locate the "Storage usage" row on App Info and click its verified clickable ancestor.
     */
    private void handleAppInfoScreen(AccessibilityNodeInfo root, AccessibilityEvent event) {
        if (currentState != State.NAVIGATING_TO_STORAGE) return;

        List<AccessibilityNodeInfo> storageRows = root.findAccessibilityNodeInfosByText("Storage usage");
        if (storageRows == null || storageRows.isEmpty()) {
            return;
        }

        if (storageRows.size() > 1) {
            fail("Ambiguity: Found multiple 'Storage usage' nodes on screen.");
            return;
        }

        AccessibilityNodeInfo storageNode = storageRows.get(0);
        CharSequence text = storageNode.getText();
        if (text == null || !"Storage usage".equalsIgnoreCase(text.toString().trim())) {
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

        log("Safeguards verified on App Info screen. Clicking 'Storage usage' ancestor: " + clickableAncestor.getClassName());
        boolean clicked = clickableAncestor.performAction(AccessibilityNodeInfo.ACTION_CLICK);
        if (!clicked) {
            fail("Click action on 'Storage usage' row returned false.");
            return;
        }

        log("Successfully clicked 'Storage usage' row. Waiting for Storage usage sub-screen...");
        transitionTo(State.INSPECTING_STORAGE, "Waiting for Storage usage screen inspection...");
        scheduleStorageInspection(500);
    }

    private void scheduleStorageInspection(final long delayMs) {
        mainHandler.postDelayed(new Runnable() {
            @Override
            public void run() {
                synchronized (PilotController.this) {
                    if (currentState == State.INSPECTING_STORAGE) {
                        SettingsInspectionAccessibilityService svc = SettingsInspectionAccessibilityService.getInstance();
                        if (svc != null) {
                            AccessibilityNodeInfo root = svc.getRootInActiveWindow();
                            if (root != null) {
                                inspectStorageUsageBeforeDialog(root, svc);
                                return;
                            }
                        }
                        scheduleStorageInspection(350);
                    }
                }
            }
        }, delayMs);
    }

    /**
     * Inspect Storage usage screen, isolate buttons, and launch Step 2 Confirmation Dialog.
     */
    private synchronized void inspectStorageUsageBeforeDialog(AccessibilityNodeInfo root, SettingsInspectionAccessibilityService service) {
        if (currentState != State.INSPECTING_STORAGE) return;

        if (service == null) {
            service = SettingsInspectionAccessibilityService.getInstance();
        }
        if (service == null) return;

        // Safeguard 1: Verify screen title "Storage usage"
        if (!hasTextNode(root, "Storage usage")) {
            return;
        }

        // Safeguard 2: Verify "Cache" heading
        if (!hasTextNode(root, "Cache")) {
            return;
        }

        // Safeguard 3: Isolate Clear data button to prove layout integrity
        List<AccessibilityNodeInfo> clearDataList = root.findAccessibilityNodeInfosByText("Clear data");
        if (clearDataList == null || clearDataList.isEmpty()) {
            return;
        }
        log("Safeguard verified: 'Clear data' button detected and isolated (will NOT be clicked).");

        // Safeguard 4: Locate exact "Clear cache" button
        List<AccessibilityNodeInfo> clearCacheList = root.findAccessibilityNodeInfosByText("Clear cache");
        if (clearCacheList == null || clearCacheList.isEmpty()) {
            return;
        }

        AccessibilityNodeInfo targetButton = null;
        for (AccessibilityNodeInfo node : clearCacheList) {
            CharSequence txt = node.getText();
            if (txt != null && "Clear cache".equalsIgnoreCase(txt.toString().trim())) {
                if ("android.widget.Button".contentEquals(node.getClassName()) || node.isClickable()) {
                    targetButton = node;
                    break;
                }
            }
        }

        if (targetButton == null) {
            return;
        }

        // Extract observed cache value near "Cache" heading
        String observedCache = extractObservedCacheSize(root);
        currentResult.cacheBefore = observedCache;
        log("Observed Instagram Cache before clearing: " + observedCache);

        // Step 8: Transition to AWAITING_FINAL_CONFIRMATION (clears timeout) and launch dialog
        transitionTo(State.AWAITING_FINAL_CONFIRMATION, "Presenting final confirmation dialog to user.");

        Intent dialogIntent = new Intent(service, PilotConfirmationDialogActivity.class);
        dialogIntent.putExtra("target_package", TARGET_PACKAGE);
        dialogIntent.putExtra("cache_before", observedCache);
        dialogIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        service.startActivity(dialogIntent);
        log("Presented final confirmation dialog to user.");
    }

    /**
     * Execute the cache clearing click after Step 2 user confirmation.
     */
    private synchronized void handleStorageUsageScreenForClick(AccessibilityNodeInfo root, AccessibilityEvent event, SettingsInspectionAccessibilityService service) {
        if (currentState != State.EXECUTING_CLEAR_CACHE) return;

        // Re-validate ALL safeguards immediately before clicking!
        if (!hasTextNode(root, "Storage usage") || !hasTextNode(root, "Cache")) {
            return;
        }

        // Verify Clear data is present and isolated
        List<AccessibilityNodeInfo> clearDataList = root.findAccessibilityNodeInfosByText("Clear data");
        if (clearDataList == null || clearDataList.isEmpty()) {
            fail("Pre-click abort: 'Clear data' button not found, layout integrity compromised.");
            return;
        }

        // Locate exact "Clear cache" button
        List<AccessibilityNodeInfo> clearCacheList = root.findAccessibilityNodeInfosByText("Clear cache");
        AccessibilityNodeInfo exactButton = null;
        int validCount = 0;

        for (AccessibilityNodeInfo node : clearCacheList) {
            CharSequence txt = node.getText();
            if (txt != null && "Clear cache".equalsIgnoreCase(txt.toString().trim())) {
                if (node.isClickable() && node.isEnabled()) {
                    exactButton = node;
                    validCount++;
                }
            }
        }

        if (validCount != 1 || exactButton == null) {
            fail("Pre-click abort: Expected exactly 1 enabled 'Clear cache' button, found: " + validCount);
            return;
        }

        String resId = exactButton.getViewIdResourceName();
        log(">>> EXECUTING ACTION: Clicking validated 'Clear cache' button! ID: " + resId);
        boolean clicked = exactButton.performAction(AccessibilityNodeInfo.ACTION_CLICK);

        if (!clicked) {
            fail("performAction(ACTION_CLICK) on 'Clear cache' returned false.");
            return;
        }

        log("Click succeeded. Transitioning to verification...");
        transitionTo(State.VERIFYING_RESULT, "Cache click executed. Verifying updated cache size...");

        // Schedule Step 10 & 11 verification after 1500ms
        mainHandler.postDelayed(new Runnable() {
            @Override
            public void run() {
                verifyCacheClearResult(service);
            }
        }, 1500);
    }

    /**
     * Verify outcome honestly and return to MainActivity.
     */
    private synchronized void verifyCacheClearResult(SettingsInspectionAccessibilityService service) {
        if (currentState != State.VERIFYING_RESULT) return;

        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        String cacheAfter = "Unknown";
        if (root != null) {
            cacheAfter = extractObservedCacheSize(root);
        }
        currentResult.cacheAfter = cacheAfter;
        log("Observed Instagram Cache after clearing: " + cacheAfter);

        String before = currentResult.cacheBefore;
        boolean verified = false;

        if (!"Unknown".equalsIgnoreCase(cacheAfter) && !"Pending".equalsIgnoreCase(cacheAfter)) {
            if ("0 B".equalsIgnoreCase(cacheAfter) || "0B".equalsIgnoreCase(cacheAfter) || "0.00 B".equalsIgnoreCase(cacheAfter)) {
                verified = true;
            } else if (!before.equalsIgnoreCase(cacheAfter)) {
                verified = true;
            }
        }

        if (verified) {
            currentResult.outcome = Outcome.SUCCESS_VERIFIED;
            currentResult.summaryMessage = String.format(Locale.US,
                    "SUCCESS_VERIFIED: Instagram cache cleared from %s to %s.", before, cacheAfter);
            log(currentResult.summaryMessage);
        } else {
            currentResult.outcome = Outcome.ACTION_ATTEMPTED_UNVERIFIED;
            currentResult.summaryMessage = String.format(Locale.US,
                    "ACTION_ATTEMPTED_UNVERIFIED: Click executed, but cache size change could not be proven (Before: %s, After: %s).",
                    before, cacheAfter);
            log(currentResult.summaryMessage);
        }

        transitionTo(State.DONE, currentResult.summaryMessage);
        clearTimeout();

        // Return to MainActivity to present the truthful outcome
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

    private String extractObservedCacheSize(AccessibilityNodeInfo root) {
        List<AccessibilityNodeInfo> cacheNodes = root.findAccessibilityNodeInfosByText("Cache");
        if (cacheNodes == null || cacheNodes.isEmpty()) return "Unknown";

        for (AccessibilityNodeInfo node : cacheNodes) {
            CharSequence text = node.getText();
            if (text != null && "Cache".equalsIgnoreCase(text.toString().trim())) {
                AccessibilityNodeInfo parent = node.getParent();
                if (parent != null) {
                    for (int i = 0; i < parent.getChildCount(); i++) {
                        AccessibilityNodeInfo child = parent.getChild(i);
                        if (child != null && child != node) {
                            CharSequence childText = child.getText();
                            if (childText != null && isSizeString(childText.toString())) {
                                return childText.toString().trim();
                            }
                        }
                    }
                }
            }
        }

        // Secondary search: find any text node containing byte units (B, KB, MB, GB)
        List<AccessibilityNodeInfo> allTextViews = new ArrayList<>();
        collectTextViews(root, allTextViews);
        for (AccessibilityNodeInfo tv : allTextViews) {
            CharSequence t = tv.getText();
            if (t != null && isSizeString(t.toString())) {
                return t.toString().trim();
            }
        }

        return "Unknown";
    }

    private boolean isSizeString(String str) {
        if (str == null) return false;
        String trimmed = str.trim();
        return trimmed.matches("(?i)^[0-9]+([.][0-9]+)?\\s*(B|KB|MB|GB)$");
    }

    private void collectTextViews(AccessibilityNodeInfo node, List<AccessibilityNodeInfo> list) {
        if (node == null) return;
        if ("android.widget.TextView".contentEquals(node.getClassName())) {
            list.add(node);
        }
        for (int i = 0; i < node.getChildCount(); i++) {
            collectTextViews(node.getChild(i), list);
        }
    }

    private boolean hasTextNode(AccessibilityNodeInfo root, String query) {
        if (root == null) return false;
        List<AccessibilityNodeInfo> list = root.findAccessibilityNodeInfosByText(query);
        if (list == null || list.isEmpty()) return false;
        for (AccessibilityNodeInfo n : list) {
            CharSequence t = n.getText();
            if (t != null && query.equalsIgnoreCase(t.toString().trim())) {
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
            clearTimeout(); // User is reading confirmation dialog; do not time out!
        } else {
            resetTimeout();
        }
        if (listener != null) {
            mainHandler.post(new Runnable() {
                @Override
                public void run() {
                    if (listener != null) listener.onPilotStateChanged(newState, message);
                }
            });
        }
    }

    private void notifyFinished() {
        if (listener != null) {
            mainHandler.post(new Runnable() {
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
            mainHandler.postDelayed(timeoutRunnable, TIMEOUT_MS);
        }
    }

    private void clearTimeout() {
        if (timeoutRunnable != null) {
            mainHandler.removeCallbacks(timeoutRunnable);
            timeoutRunnable = null;
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
