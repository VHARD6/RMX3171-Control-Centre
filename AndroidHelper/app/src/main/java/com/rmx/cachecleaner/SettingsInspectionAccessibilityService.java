package com.rmx.cachecleaner;

import android.accessibilityservice.AccessibilityService;
import android.content.Intent;
import android.util.Log;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Phase 4.1: Accessibility Feasibility Diagnostic Service
 * 
 * STRICT DIAGNOSTIC ONLY:
 * - NO automated clicking or touch injection.
 * - NO cache deletion or data clearing.
 * - NO navigation manipulation.
 * - Passive inspection of Settings UI nodes matching cache/storage keywords.
 */
public class SettingsInspectionAccessibilityService extends AccessibilityService {

    public static final String TAG = "RMX_A11Y_PROBE";
    public static final String ACTION_SERVICE_STATE_CHANGED = "com.rmx.cachecleaner.PROBE_STATE_CHANGED";

    private static volatile boolean isRunning = false;
    private static volatile SettingsInspectionAccessibilityService instance = null;
    private long lastInspectionTime = 0;
    private String lastLoggedSnapshot = "";
    private final SimpleDateFormat timeFormat = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US);

    // Keywords relevant for cache and storage diagnostics
    private static final String[] TARGET_KEYWORDS = {
            "storage", "cache", "clear", "usage", "data", "space", "wipe"
    };

    public static boolean isServiceRunning() {
        return isRunning;
    }

    public static SettingsInspectionAccessibilityService getInstance() {
        return instance;
    }

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        isRunning = true;
        instance = this;
        Log.i(TAG, "=======================================================");
        Log.i(TAG, "SettingsInspectionAccessibilityService CONNECTED and ACTIVE.");
        Log.i(TAG, "Diagnostic mode only: No actions or clicks will be executed.");
        Log.i(TAG, "=======================================================");
        broadcastState();
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null) return;

        // Delegate event to PilotController for Phase 4.2 single-app pilot test
        PilotController.getInstance().onAccessibilityEvent(event, this);

        CharSequence pkgName = event.getPackageName();
        if (pkgName != null) {
            String pkg = pkgName.toString();
            // Filter out system UI notifications/lockscreen and own app
            if (pkg.equals("com.android.systemui") || pkg.equals("com.rmx.cachecleaner")) {
                return;
            }
        }

        int eventType = event.getEventType();
        long now = System.currentTimeMillis();

        if (eventType == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) {
            // New window or screen entered - reset snapshot to ensure fresh capture
            lastLoggedSnapshot = "";
        } else if (eventType == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED && (now - lastInspectionTime < 300)) {
            // Throttle rapid repeated content change events
            return;
        }
        lastInspectionTime = now;

        Set<String> matchedNodes = new HashSet<>();
        Set<AccessibilityNodeInfo> rootsToInspect = new HashSet<>();

        // 1. Inspect active root window
        AccessibilityNodeInfo rootNode = getRootInActiveWindow();
        if (rootNode != null) {
            rootsToInspect.add(rootNode);
        }

        // 2. Inspect source node and its window root
        AccessibilityNodeInfo source = event.getSource();
        if (source != null) {
            AccessibilityNodeInfo sourceRoot = findWindowRoot(source);
            if (sourceRoot != null) {
                rootsToInspect.add(sourceRoot);
            }
            rootsToInspect.add(source);
        }

        // 3. Inspect application windows if accessible
        try {
            List<AccessibilityWindowInfo> windows = getWindows();
            if (windows != null) {
                for (AccessibilityWindowInfo window : windows) {
                    if (window.getType() == AccessibilityWindowInfo.TYPE_APPLICATION) {
                        AccessibilityNodeInfo winRoot = window.getRoot();
                        if (winRoot != null) {
                            rootsToInspect.add(winRoot);
                        }
                    }
                }
            }
        } catch (Throwable ignored) {}

        for (AccessibilityNodeInfo r : rootsToInspect) {
            inspectNodeHierarchy(r, matchedNodes, 0);
        }

        if (!matchedNodes.isEmpty()) {
            StringBuilder snapshotBuilder = new StringBuilder();
            CharSequence className = event.getClassName();
            snapshotBuilder.append(pkgName).append("|").append(className).append("|");
            for (String s : matchedNodes) {
                snapshotBuilder.append(s).append(";");
            }
            String currentSnapshot = snapshotBuilder.toString();

            // Deduplicate identical consecutive reports
            if (!currentSnapshot.equals(lastLoggedSnapshot)) {
                lastLoggedSnapshot = currentSnapshot;

                String timestampStr = timeFormat.format(new Date(now));
                Log.i(TAG, "-------------------------------------------------------");
                Log.i(TAG, "[PROBE TIME] " + timestampStr + " (" + now + ")");
                Log.i(TAG, "[PROBE WINDOW] Package: " + pkgName + " | Screen/EventClass: " + className);
                Log.i(TAG, "[PROBE SUMMARY] Discovered " + matchedNodes.size() + " storage/cache node(s):");
                for (String detail : matchedNodes) {
                    Log.i(TAG, "   -> " + detail);
                }
                Log.i(TAG, "-------------------------------------------------------");
            }
        }
    }

    private AccessibilityNodeInfo findWindowRoot(AccessibilityNodeInfo node) {
        if (node == null) return null;
        AccessibilityNodeInfo current = node;
        int safety = 0;
        while (current.getParent() != null && safety < 30) {
            current = current.getParent();
            safety++;
        }
        return current;
    }

    private void inspectNodeHierarchy(AccessibilityNodeInfo node, Set<String> matchedNodes, int depth) {
        if (node == null || depth > 25) return;

        CharSequence text = node.getText();
        CharSequence desc = node.getContentDescription();
        String viewId = node.getViewIdResourceName();
        CharSequence nodeClass = node.getClassName();

        boolean matchesKeyword = containsTargetKeyword(text) ||
                                 containsTargetKeyword(desc) ||
                                 containsTargetKeyword(viewId);

        if (matchesKeyword) {
            boolean isClickable = node.isClickable();
            String clickableAncestor = isClickable ? "SELF" : findClickableAncestor(node);

            String summary = String.format(Locale.US,
                    "ID: [%s] | Class: [%s] | Text: \"%s\" | Desc: \"%s\" | Clickable: %b | Enabled: %b | ClickableAncestor: %s",
                    viewId != null ? viewId : "NONE",
                    nodeClass != null ? nodeClass : "unknown",
                    text != null ? text.toString().trim() : "",
                    desc != null ? desc.toString().trim() : "",
                    isClickable,
                    node.isEnabled(),
                    clickableAncestor
            );
            matchedNodes.add(summary);
        }

        int childCount = node.getChildCount();
        for (int i = 0; i < childCount; i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child != null) {
                inspectNodeHierarchy(child, matchedNodes, depth + 1);
            }
        }
    }

    private String findClickableAncestor(AccessibilityNodeInfo node) {
        if (node == null) return "NONE";
        AccessibilityNodeInfo current = node.getParent();
        int levels = 1;
        while (current != null && levels <= 6) {
            if (current.isClickable()) {
                String ancId = current.getViewIdResourceName();
                CharSequence ancClass = current.getClassName();
                return String.format(Locale.US,
                        "Level +%d [Class: %s | ID: %s | Enabled: %b]",
                        levels,
                        ancClass != null ? ancClass : "unknown",
                        ancId != null ? ancId : "NONE",
                        current.isEnabled()
                );
            }
            current = current.getParent();
            levels++;
        }
        return "NONE";
    }

    private boolean containsTargetKeyword(CharSequence text) {
        if (text == null) return false;
        String lower = text.toString().toLowerCase(Locale.ROOT);
        for (String kw : TARGET_KEYWORDS) {
            if (lower.contains(kw)) {
                return true;
            }
        }
        return false;
    }

    @Override
    public void onInterrupt() {
        Log.i(TAG, "SettingsInspectionAccessibilityService INTERRUPTED.");
    }

    @Override
    public boolean onUnbind(Intent intent) {
        isRunning = false;
        instance = null;
        Log.i(TAG, "SettingsInspectionAccessibilityService UNBOUND / STOPPED.");
        broadcastState();
        return super.onUnbind(intent);
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        isRunning = false;
        instance = null;
        Log.i(TAG, "SettingsInspectionAccessibilityService DESTROYED.");
        broadcastState();
    }

    private void broadcastState() {
        Intent intent = new Intent(ACTION_SERVICE_STATE_CHANGED);
        intent.putExtra("is_running", isRunning);
        sendBroadcast(intent);
    }
}
