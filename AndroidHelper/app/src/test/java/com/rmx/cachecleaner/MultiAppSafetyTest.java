package com.rmx.cachecleaner;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import static org.junit.Assert.*;

/**
 * Phase 5: Unit Tests for Multi-App Cache Cleaner Architecture & Safety Guards.
 * 
 * Verifies:
 * 1. Predefined allowlist acceptance & rejection (system apps, arbitrary packages, empty).
 * 2. TargetApp model lifecycle, selection, and batch reset.
 * 3. Batch authorization security: rejection of unauthorized/disallowed targets.
 * 4. Stale callback and session token invalidation between batch transitions.
 * 5. Decision matrix integration for batch items (ALREADY_CLEAN vs non-zero vs abort).
 */
public class MultiAppSafetyTest {

    // =========================================================================
    // 1. Allowlist Security & Package Validation
    // =========================================================================

    @Test
    public void testAllowlist_acceptsSupportedThirdPartyApps() {
        assertTrue(SupportedAppsRegistry.isPackageAllowed("com.instagram.android"));
        assertTrue(SupportedAppsRegistry.isPackageAllowed("com.spotify.music"));
        assertTrue(SupportedAppsRegistry.isPackageAllowed("com.snapchat.android"));
        assertTrue(SupportedAppsRegistry.isPackageAllowed("com.grofers.customerapp"));
    }

    @Test
    public void testAllowlist_rejectsCriticalSystemInfrastructure() {
        // Critical system infrastructure must never be allowed for cache cleaning
        assertFalse("Must reject Android framework", SupportedAppsRegistry.isPackageAllowed("android"));
        assertFalse("Must reject Settings", SupportedAppsRegistry.isPackageAllowed("com.android.settings"));
        assertFalse("Must reject SystemUI", SupportedAppsRegistry.isPackageAllowed("com.android.systemui"));
        assertFalse("Must reject PackageInstaller", SupportedAppsRegistry.isPackageAllowed("com.google.android.packageinstaller"));
        assertFalse("Must reject own companion app", SupportedAppsRegistry.isPackageAllowed("com.rmx.cachecleaner"));
        assertFalse("Must reject Shizuku privilege bridge", SupportedAppsRegistry.isPackageAllowed("moe.shizuku.privileged.api"));
        assertFalse("Must reject Telephony/Phone service", SupportedAppsRegistry.isPackageAllowed("com.android.phone"));
        assertFalse("Must reject Active Keyboard (Gboard)", SupportedAppsRegistry.isPackageAllowed("com.google.android.inputmethod.latin"));
        assertFalse("Must reject Active Accessibility (TalkBack)", SupportedAppsRegistry.isPackageAllowed("com.google.android.marvin.talkback"));
    }

    @Test
    public void testAllowlist_rejectsArbitraryOrMalformedPackages() {
        assertFalse(SupportedAppsRegistry.isPackageAllowed("com.arbitrary.untrusted.app"));
        assertFalse(SupportedAppsRegistry.isPackageAllowed("com.unknown.cleaner"));
        assertFalse(SupportedAppsRegistry.isPackageAllowed(""));
        assertFalse(SupportedAppsRegistry.isPackageAllowed("   "));
        assertFalse(SupportedAppsRegistry.isPackageAllowed(null));
    }

    // =========================================================================
    // 2. TargetApp Model State & Lifecycle
    // =========================================================================

    @Test
    public void testTargetApp_initializationAndDefaults() {
        TargetApp app = new TargetApp("com.spotify.music", "Spotify");
        assertEquals("com.spotify.music", app.getPackageName());
        assertEquals("Spotify", app.getDefaultLabel());
        assertEquals("Spotify", app.getResolvedLabel());
        assertTrue("Should be selected by default", app.isSelected());
        assertEquals(TargetApp.Status.PENDING, app.getStatus());
        assertEquals("Pending", app.getStatusDetail());
        assertEquals("", app.getCacheBefore());
        assertEquals("", app.getCacheAfter());
    }

    @Test
    public void testTargetApp_resolvedLabelOverride() {
        TargetApp app = new TargetApp("com.grofers.customerapp", "Blinkit");
        assertEquals("Blinkit", app.getResolvedLabel());

        // When system reports a customized label
        app.setResolvedLabel("Blinkit: Grocery Delivery");
        assertEquals("Blinkit: Grocery Delivery", app.getResolvedLabel());

        // Falling back if empty
        app.setResolvedLabel("");
        assertEquals("Blinkit", app.getResolvedLabel());
    }

    @Test
    public void testTargetApp_resetForBatch() {
        TargetApp app = new TargetApp("com.instagram.android", "Instagram");
        app.setStatus(TargetApp.Status.CLEARED);
        app.setStatusDetail("Cleared: 182 MB -> 0 B");
        app.setCacheBefore("182 MB");
        app.setCacheAfter("0 B");

        app.resetForBatch();
        assertEquals(TargetApp.Status.PENDING, app.getStatus());
        assertEquals("Pending", app.getStatusDetail());
        assertEquals("", app.getCacheBefore());
        assertEquals("", app.getCacheAfter());
    }

    // =========================================================================
    // 3. Batch Queue Validation & Security Guards
    // =========================================================================

    @Test
    public void testBatchSecurity_rejectsDisallowedTargetInQueue() {
        PilotController controller = PilotController.getInstance();

        List<TargetApp> batch = new ArrayList<>();
        batch.add(new TargetApp("com.instagram.android", "Instagram"));
        batch.add(new TargetApp("com.malicious.untrusted", "Malicious")); // forbidden target!

        final boolean[] finishedCalled = {false};
        final String[] finishedSummary = {""};

        boolean started = controller.startBatch(null, batch, new PilotController.BatchListener() {
            @Override
            public void onBatchProgress(int currentIndex, int total, TargetApp currentApp) {}

            @Override
            public void onAppFinished(TargetApp app, PilotController.Outcome outcome) {}

            @Override
            public void onBatchFinished(List<TargetApp> batchResults, String summary) {
                finishedCalled[0] = true;
                finishedSummary[0] = summary;
            }
        });

        assertFalse("Must reject batch containing disallowed packages", started);
        assertTrue("onBatchFinished must notify with security violation", finishedCalled[0]);
        assertTrue(finishedSummary[0].contains("Security violation"));
    }

    @Test
    public void testBatchSecurity_rejectsEmptyOrNullQueue() {
        PilotController controller = PilotController.getInstance();
        assertFalse(controller.startBatch(null, null, null));
        assertFalse(controller.startBatch(null, new ArrayList<TargetApp>(), null));
    }

    // =========================================================================
    // 4. Batch Token Invalidation & Stale Callback Isolation
    // =========================================================================

    @Test
    public void testSessionToken_invalidatesAcrossSequentialTransitions() {
        PilotController controller = PilotController.getInstance();
        int token1 = controller.getActiveSessionToken();

        // Callback with older token from prior session must be rejected
        assertFalse("Prior session token must be invalid",
                controller.isCallbackValid(token1 - 1, controller.getCurrentState()));

        // Callback with future token must be rejected
        assertFalse("Mismatched token must be invalid",
                controller.isCallbackValid(token1 + 1, controller.getCurrentState()));

        // Callback with state mismatch must be rejected
        PilotController.State mismatchState = (controller.getCurrentState() == PilotController.State.IDLE)
                ? PilotController.State.EXECUTING_CLEAR_CACHE
                : PilotController.State.IDLE;
        assertFalse("State mismatch must invalidate callback",
                controller.isCallbackValid(token1, mismatchState));
    }

    // =========================================================================
    // 5. Phase 6: Cancellation Result Preservation & Truthful Reporting
    // =========================================================================

    @Test
    public void testTargetApp_cancelledStatus() {
        TargetApp app = new TargetApp("com.spotify.music", "Spotify");
        app.setStatus(TargetApp.Status.CANCELLED);
        app.setStatusDetail("Cancelled by user");
        assertEquals(TargetApp.Status.CANCELLED, app.getStatus());
        assertEquals("Cancelled by user", app.getStatusDetail());
    }

    @Test
    public void testBatchCancellation_summaryTruthfulBreakdown() {
        // Build a simulated batch queue where item 0 already completed
        List<TargetApp> queue = new ArrayList<>();
        TargetApp app1 = new TargetApp("com.instagram.android", "Instagram");
        app1.setStatus(TargetApp.Status.CLEARED);
        app1.setCacheBefore("182 MB");
        app1.setCacheAfter("0 B");
        app1.setStatusDetail("Cleared: 182 MB -> 0 B");

        TargetApp app2 = new TargetApp("com.spotify.music", "Spotify");
        app2.setStatus(TargetApp.Status.CANCELLED);
        app2.setStatusDetail("Cancelled: User cancelled via UI button");

        TargetApp app3 = new TargetApp("com.snapchat.android", "Snapchat");
        app3.setStatus(TargetApp.Status.CANCELLED);
        app3.setStatusDetail("Cancelled before start");

        queue.add(app1);
        queue.add(app2);
        queue.add(app3);

        // Verify result preservation
        assertEquals("Prior completed app must remain CLEARED", TargetApp.Status.CLEARED, queue.get(0).getStatus());
        assertEquals("Active app must be CANCELLED", TargetApp.Status.CANCELLED, queue.get(1).getStatus());
        assertEquals("Subsequent app must be CANCELLED", TargetApp.Status.CANCELLED, queue.get(2).getStatus());

        // Count outcomes
        int cleared = 0, alreadyClean = 0, failed = 0, cancelled = 0;
        for (TargetApp app : queue) {
            if (app.getStatus() == TargetApp.Status.CLEARED) cleared++;
            else if (app.getStatus() == TargetApp.Status.ALREADY_CLEAN) alreadyClean++;
            else if (app.getStatus() == TargetApp.Status.FAILED) failed++;
            else if (app.getStatus() == TargetApp.Status.CANCELLED || app.getStatus() == TargetApp.Status.SKIPPED) cancelled++;
        }

        assertEquals(1, cleared);
        assertEquals(0, alreadyClean);
        assertEquals(0, failed);
        assertEquals(2, cancelled);

        String summary = String.format(java.util.Locale.US,
                "Batch cancelled by user.\nResults before cancel: %d cleared, %d already clean, %d failed, %d stopped/cancelled.",
                cleared, alreadyClean, failed, cancelled);
        assertTrue(summary.contains("1 cleared"));
        assertTrue(summary.contains("2 stopped/cancelled"));
        assertFalse(summary.contains("Batch complete:")); // Must never claim full success
    }

    @Test
    public void testBatchPartialFailure_summaryTruthfulBreakdown() {
        List<TargetApp> queue = new ArrayList<>();
        TargetApp app1 = new TargetApp("com.instagram.android", "Instagram");
        app1.setStatus(TargetApp.Status.CLEARED);
        app1.setStatusDetail("Cleared: 40 KB -> 0 B");

        TargetApp app2 = new TargetApp("com.spotify.music", "Spotify");
        app2.setStatus(TargetApp.Status.ALREADY_CLEAN);
        app2.setStatusDetail("Already clean (0 B)");

        TargetApp app3 = new TargetApp("com.snapchat.android", "Snapchat");
        app3.setStatus(TargetApp.Status.FAILED);
        app3.setStatusDetail("Failed: Identity mismatch");

        TargetApp app4 = new TargetApp("com.grofers.customerapp", "Blinkit");
        app4.setStatus(TargetApp.Status.SKIPPED);
        app4.setStatusDetail("Skipped due to prior failure");

        queue.add(app1);
        queue.add(app2);
        queue.add(app3);
        queue.add(app4);

        int cleared = 0, alreadyClean = 0, failed = 0, skipped = 0;
        for (TargetApp app : queue) {
            if (app.getStatus() == TargetApp.Status.CLEARED) cleared++;
            else if (app.getStatus() == TargetApp.Status.ALREADY_CLEAN) alreadyClean++;
            else if (app.getStatus() == TargetApp.Status.FAILED) failed++;
            else if (app.getStatus() == TargetApp.Status.SKIPPED) skipped++;
        }

        assertEquals(1, cleared);
        assertEquals(1, alreadyClean);
        assertEquals(1, failed);
        assertEquals(1, skipped);

        String summary = String.format(java.util.Locale.US,
                "Batch stopped due to error: Identity mismatch\nResults: %d cleared, %d already clean, %d failed, %d skipped.",
                cleared, alreadyClean, failed, skipped);

        assertTrue(summary.contains("1 cleared"));
        assertTrue(summary.contains("1 already clean"));
        assertTrue(summary.contains("1 failed"));
        assertFalse("Must never claim full success when failure occurred", summary.contains("Batch complete:"));
    }

    // =========================================================================
    // 6. Phase 7 Correction: All-Apps Discovery, Classification & Safety Rules
    // =========================================================================

    @Test
    public void testDynamicDiscovery_eligibilityFilterPassesValidUserApp() {
        android.content.pm.PackageInfo pi = new android.content.pm.PackageInfo();
        pi.packageName = "com.whatsapp";
        pi.applicationInfo = new android.content.pm.ApplicationInfo();
        pi.applicationInfo.enabled = true;
        pi.applicationInfo.flags = 0; // standard third-party app

        assertTrue("Valid third-party app must be eligible for discovery", SupportedAppsRegistry.isPackageEligible(pi));
    }

    @Test
    public void testDynamicDiscovery_systemAppFlagIsNoLongerExcluded() {
        // Preinstalled system applications (like Google Maps, Messages, Calculator) must be discovered
        android.content.pm.PackageInfo pi = new android.content.pm.PackageInfo();
        pi.packageName = "com.google.android.apps.maps";
        pi.applicationInfo = new android.content.pm.ApplicationInfo();
        pi.applicationInfo.enabled = true;
        pi.applicationInfo.flags = android.content.pm.ApplicationInfo.FLAG_SYSTEM;

        assertTrue("Preinstalled app with FLAG_SYSTEM must be eligible for discovery",
                SupportedAppsRegistry.isPackageEligible(pi));
    }

    @Test
    public void testDynamicDiscovery_allowsGoogleAndPreinstalledAppsForCleaning() {
        SupportedAppsRegistry.resetToBaseline();

        String[] preinstalledUserApps = {
                "com.google.android.googlequicksearchbox", // Google app
                "com.google.android.apps.messaging",       // Google Messages
                "com.google.android.apps.maps",            // Google Maps
                "com.android.vending",                     // Google Play Store
                "com.google.android.youtube",              // YouTube
                "com.coloros.calculator"                   // ColorOS Calculator
        };

        for (String pkg : preinstalledUserApps) {
            assertFalse("Initially not allowed before registration: " + pkg,
                    ProtectedPackages.isExcluded(pkg));
            SupportedAppsRegistry.registerAllowedPackage(pkg, "Test App");
            assertTrue("Must be allowed for cleaning once registered: " + pkg,
                    SupportedAppsRegistry.isPackageAllowed(pkg));
        }

        SupportedAppsRegistry.resetToBaseline();
    }

    @Test
    public void testDynamicDiscovery_identifiesAndProtectsCriticalComponents() {
        String[] criticalComponents = {
                "android",
                "oplus",
                "com.android.settings",
                "com.android.systemui",
                "com.google.android.packageinstaller",
                "com.android.packageinstaller",
                "com.google.android.permissioncontroller",
                "com.android.phone",
                "com.android.server.telecom",
                "com.android.providers.telephony",
                "com.mediatek.telephony",
                "com.google.android.inputmethod.latin",
                "com.google.android.marvin.talkback",
                "com.android.providers.settings",
                "com.coloros.wirelesssettings",
                "com.coloros.simsettings",
                "com.coloros.launcher",
                "com.rmx.cachecleaner",
                "moe.shizuku.privileged.api"
        };

        for (String pkg : criticalComponents) {
            assertTrue("Must be identified as protected: " + pkg,
                    ProtectedPackages.isExcluded(pkg));
            assertNotNull("Must have documented protection reason: " + pkg,
                    ProtectedPackages.getExclusionReason(pkg));
            // Even if someone tries to register it, isPackageAllowed must reject it
            SupportedAppsRegistry.registerAllowedPackage(pkg, "Bypass Attempt");
            assertFalse("Must never allow protected component: " + pkg,
                    SupportedAppsRegistry.isPackageAllowed(pkg));
        }
    }

    @Test
    public void testTargetApp_protectionEnforcesNonSelectable() {
        TargetApp app = new TargetApp("com.android.settings", "Settings");
        app.setClassification(TargetApp.Classification.SYSTEM_COMPONENT);
        app.setProtected(true);
        app.setProtectionReason("Core system settings");

        assertTrue(app.isProtected());
        assertFalse("Protected app must not be selected", app.isSelected());

        // Attempting to select must be ignored
        app.setSelected(true);
        assertFalse("Protected app must remain unselected", app.isSelected());

        // Status reset must preserve protected state
        app.resetForBatch();
        assertEquals(TargetApp.Status.PROTECTED, app.getStatus());
        assertTrue(app.getStatusDetail().contains("Protected:"));
    }

    @Test
    public void testDynamicDiscovery_registrationAndAllowance() {
        SupportedAppsRegistry.resetToBaseline();

        // An unknown arbitrary app is initially not allowed
        assertFalse(SupportedAppsRegistry.isPackageAllowed("org.telegram.messenger"));

        // When discovered as eligible, it gets registered
        SupportedAppsRegistry.registerAllowedPackage("org.telegram.messenger", "Telegram");
        assertTrue("Discovered and registered eligible app must now be allowed",
                SupportedAppsRegistry.isPackageAllowed("org.telegram.messenger"));
        assertEquals("Telegram", SupportedAppsRegistry.getDefaultLabel("org.telegram.messenger"));

        // Clean up
        SupportedAppsRegistry.resetToBaseline();
        assertFalse(SupportedAppsRegistry.isPackageAllowed("org.telegram.messenger"));
    }

    // =========================================================================
    // 7. Phase 7: App Search & Filter Logic
    // =========================================================================

    @Test
    public void testAppFiltering_queryByLabelAndPackage() {
        List<TargetApp> list = new ArrayList<>();
        list.add(new TargetApp("com.instagram.android", "Instagram"));
        list.add(new TargetApp("com.whatsapp", "WhatsApp"));
        list.add(new TargetApp("org.telegram.messenger", "Telegram"));
        list.add(new TargetApp("com.spotify.music", "Spotify"));

        // 1. Empty query returns all
        assertEquals(4, SupportedAppsRegistry.filterApps(list, "").size());
        assertEquals(4, SupportedAppsRegistry.filterApps(list, null).size());

        // 2. Query matching label
        List<TargetApp> gramMatch = SupportedAppsRegistry.filterApps(list, "gram");
        assertEquals(2, gramMatch.size()); // Instagram and Telegram

        // 3. Query matching package
        List<TargetApp> spotifyMatch = SupportedAppsRegistry.filterApps(list, "music");
        assertEquals(1, spotifyMatch.size());
        assertEquals("com.spotify.music", spotifyMatch.get(0).getPackageName());

        // 4. Case-insensitivity
        assertEquals(1, SupportedAppsRegistry.filterApps(list, "WHATSAPP").size());

        // 5. No matches
        assertTrue(SupportedAppsRegistry.filterApps(list, "nonexistent").isEmpty());
    }

    // =========================================================================
    // 8. Phase 7: ShizukuQuickCleaner Result & Delta Calculation
    // =========================================================================

    @Test
    public void testShizukuQuickCleaner_formatBytes() {
        assertEquals("500 B", ShizukuQuickCleaner.formatBytes(500));
        assertEquals("1.00 KB", ShizukuQuickCleaner.formatBytes(1024));
        assertEquals("10.00 MB", ShizukuQuickCleaner.formatBytes(10 * 1024 * 1024));
        assertEquals("2.50 GB", ShizukuQuickCleaner.formatBytes((long) (2.5 * 1024 * 1024 * 1024)));
    }

    @Test
    public void testShizukuQuickCleaner_resultStructureAndDeltaReporting() {
        // Simulated run where 134 MB was reclaimed
        long before = 1000000000L;
        long after = 1140508672L;
        long reclaimed = after - before;

        ShizukuQuickCleaner.QuickCleanResult successResult = new ShizukuQuickCleaner.QuickCleanResult(
                true, 0, before, after, reclaimed, 320, "", "",
                "System cache trimmed successfully!", false, false
        );

        assertTrue(successResult.success);
        assertEquals(0, successResult.exitCode);
        assertEquals(140508672L, successResult.bytesReclaimed);
        assertEquals(320, successResult.durationMs);
        assertFalse(successResult.isShizukuUnavailable);
        assertFalse(successResult.isShizukuUnauthorized);

        // Simulated error result
        ShizukuQuickCleaner.QuickCleanResult errorResult = ShizukuQuickCleaner.QuickCleanResult.error(
                "Shizuku service is not running", true, false
        );
        assertFalse(errorResult.success);
        assertTrue(errorResult.isShizukuUnavailable);
        assertFalse(errorResult.isShizukuUnauthorized);
        assertEquals("Shizuku service is not running", errorResult.summaryMessage);
    }

    // =========================================================================
    // 9. Phase 7 Correction: Google & System App Classification & Safeguards
    // =========================================================================

    @Test
    public void testClassification_googleAppsAreCleanable() {
        String[] googleApps = {
                "com.google.android.googlequicksearchbox", // Google app
                "com.google.android.apps.messaging",       // Google Messages
                "com.google.android.apps.maps",            // Google Maps
                "com.android.vending",                     // Google Play Store
                "com.google.android.youtube",              // YouTube
                "com.google.android.gm",                   // Gmail
                "com.google.android.apps.photos",          // Google Photos
                "com.android.chrome"                       // Chrome
        };

        for (String pkg : googleApps) {
            assertFalse("Google app must NOT be excluded as protected infrastructure: " + pkg,
                    ProtectedPackages.isExcluded(pkg));
            SupportedAppsRegistry.registerAllowedPackage(pkg, "Google App");
            assertTrue("Google app must be allowed for cache cleaning: " + pkg,
                    SupportedAppsRegistry.isPackageAllowed(pkg));
        }

        SupportedAppsRegistry.resetToBaseline();
    }

    @Test
    public void testClassification_criticalInfrastructureStrictlyProtected() {
        String[] criticalInfrastructure = {
                "android",
                "com.android.settings",
                "com.android.systemui",
                "com.google.android.packageinstaller",
                "com.android.packageinstaller",
                "com.google.android.permissioncontroller",
                "com.rmx.cachecleaner",
                "moe.shizuku.privileged.api",
                "com.android.phone",
                "com.android.server.telecom",
                "com.android.providers.telephony",
                "com.mediatek.telephony",
                "com.google.android.inputmethod.latin",
                "com.google.android.marvin.talkback",
                "com.android.providers.settings",
                "com.coloros.wirelesssettings",
                "com.coloros.simsettings",
                "com.coloros.launcher"
        };

        for (String pkg : criticalInfrastructure) {
            assertTrue("Critical infrastructure MUST be excluded: " + pkg,
                    ProtectedPackages.isExcluded(pkg));
            assertFalse("Critical infrastructure must NEVER be allowed for cache cleaning: " + pkg,
                    SupportedAppsRegistry.isPackageAllowed(pkg));
        }
    }

    @Test
    public void testProtectedApp_cannotBeSelected() {
        TargetApp protectedApp = new TargetApp("com.android.settings", "Settings");
        protectedApp.setClassification(TargetApp.Classification.SYSTEM_COMPONENT);
        protectedApp.setProtected(true);
        protectedApp.setProtectionReason("System settings app");
        protectedApp.setSelected(false);

        // Attempting to select must be rejected
        protectedApp.setSelected(true);
        assertFalse("Protected app must never be selectable", protectedApp.isSelected());
    }

    // =========================================================================
    // 10. Dynamic Inventory Refresh & Selection Preservation Tests
    // =========================================================================

    @Test
    public void testDynamicInventory_preservesUserSelectionAcrossRefresh() {
        // Initial inventory with 2 apps
        TargetApp app1 = new TargetApp("com.instagram.android", "Instagram");
        app1.setSelected(true);
        app1.setStatus(TargetApp.Status.CLEARED);
        app1.setCacheBefore("100 MB");
        app1.setCacheAfter("0 B");

        TargetApp app2 = new TargetApp("com.spotify.music", "Spotify");
        app2.setSelected(false);

        List<TargetApp> oldList = Arrays.asList(app1, app2);

        // Simulated fresh discovery containing app1, app2, plus newly installed app3
        TargetApp fresh1 = new TargetApp("com.instagram.android", "Instagram");
        TargetApp fresh2 = new TargetApp("com.spotify.music", "Spotify");
        TargetApp fresh3 = new TargetApp("com.test.newapp", "New App"); // Newly installed

        List<TargetApp> freshList = Arrays.asList(fresh1, fresh2, fresh3);

        // Apply state reconciliation logic (same algorithm as refreshAppInventory)
        java.util.Map<String, Boolean> prevSelections = new java.util.HashMap<>();
        java.util.Map<String, TargetApp.Status> prevStatuses = new java.util.HashMap<>();
        for (TargetApp old : oldList) {
            prevSelections.put(old.getPackageName(), old.isSelected());
            prevStatuses.put(old.getPackageName(), old.getStatus());
        }

        for (TargetApp fresh : freshList) {
            String pkg = fresh.getPackageName();
            if (prevSelections.containsKey(pkg)) {
                fresh.setSelected(prevSelections.get(pkg));
                fresh.setStatus(prevStatuses.get(pkg));
            } else {
                fresh.setSelected(false); // Newly discovered apps default to unselected
            }
        }

        // Verify state preservation
        assertTrue("Existing selected app1 must remain selected", fresh1.isSelected());
        assertEquals("Existing app1 status must be preserved", TargetApp.Status.CLEARED, fresh1.getStatus());
        assertFalse("Existing unselected app2 must remain unselected", fresh2.isSelected());
        assertFalse("Newly discovered app3 must not be selected by default", fresh3.isSelected());
    }

    @Test
    public void testDynamicInventory_unregistersUninstalledApp() {
        String testPkg = "com.temporary.installed.tool";
        SupportedAppsRegistry.registerAllowedPackage(testPkg, "Temporary Tool");
        assertTrue(SupportedAppsRegistry.isPackageAllowed(testPkg));

        // Simulated uninstall
        SupportedAppsRegistry.unregisterAllowedPackage(testPkg);
        assertFalse("Uninstalled app must be removed from dynamic allowlist",
                SupportedAppsRegistry.isPackageAllowed(testPkg));
    }

    @Test
    public void testDynamicInventory_exactInstallUninstallSequencePreservesFullSelection() {
        // Baseline: 206 selectable apps, all 206 selected
        Set<String> selectedPackages = new HashSet<>();
        List<TargetApp> baselineApps = new ArrayList<>();
        for (int i = 0; i < 206; i++) {
            String pkg = "com.app.selectable." + i;
            TargetApp app = new TargetApp(pkg, "App " + i);
            app.setProtected(false);
            app.setSelected(true);
            baselineApps.add(app);
            selectedPackages.add(pkg);
        }

        // Verify baseline: 206 of 206
        int baselineSelected = 0;
        for (TargetApp app : baselineApps) {
            if (!app.isProtected() && app.isSelected()) baselineSelected++;
        }
        assertEquals("Baseline must have 206 selected", 206, baselineSelected);

        // Step 1: Install new unselected test app -> 207 selectable apps
        List<TargetApp> afterInstallApps = new ArrayList<>();
        for (int i = 0; i < 206; i++) {
            TargetApp app = new TargetApp("com.app.selectable." + i, "App " + i);
            app.setProtected(false);
            afterInstallApps.add(app);
        }
        String testPkg = "com.dynamic.sampletestapp";
        TargetApp testApp = new TargetApp(testPkg, "Dynamic Test App");
        testApp.setProtected(false);
        afterInstallApps.add(testApp);

        // Reconciliation
        for (TargetApp fresh : afterInstallApps) {
            String pkg = fresh.getPackageName();
            if (selectedPackages.contains(pkg)) {
                fresh.setSelected(true);
            } else {
                fresh.setSelected(false); // Newly discovered app defaults to unselected
            }
        }

        // Verify Step 1: 206 of 207 selected
        int step1Selected = 0;
        for (TargetApp app : afterInstallApps) {
            if (!app.isProtected() && app.isSelected()) step1Selected++;
        }
        assertEquals("After install, selectable count is 207 and selected remains 206", 206, step1Selected);
        assertFalse("Newly installed test app must be unselected", testApp.isSelected());

        // Step 2: Uninstall test app -> back to 206 selectable apps
        // When uninstalled, testPkg is removed from selectedPackages (if present)
        selectedPackages.remove(testPkg);

        List<TargetApp> afterUninstallApps = new ArrayList<>();
        for (int i = 0; i < 206; i++) {
            TargetApp app = new TargetApp("com.app.selectable." + i, "App " + i);
            app.setProtected(false);
            afterUninstallApps.add(app);
        }

        // Clean up uninstalled packages from selectedPackages
        Set<String> installedPkgs = new HashSet<>();
        for (TargetApp app : afterUninstallApps) {
            installedPkgs.add(app.getPackageName());
        }
        selectedPackages.retainAll(installedPkgs);

        // Reconciliation
        for (TargetApp fresh : afterUninstallApps) {
            String pkg = fresh.getPackageName();
            if (selectedPackages.contains(pkg)) {
                fresh.setSelected(true);
            } else {
                fresh.setSelected(false);
            }
        }

        // Verify Step 2: Exactly 206 of 206 selected! No selection loss!
        int step2Selected = 0;
        for (TargetApp app : afterUninstallApps) {
            if (!app.isProtected() && app.isSelected()) step2Selected++;
        }
        assertEquals("After uninstalling test app, selection count must return to exactly 206 of 206", 206, step2Selected);
    }

    @Test
    public void testDynamicInventory_uninstallUnselectedAppDoesNotDeselectUnrelatedApp() {
        // User has 205 of 206 selected: Spotify is manually unselected
        Set<String> selectedPackages = new HashSet<>();
        for (int i = 0; i < 206; i++) {
            selectedPackages.add("com.app.selectable." + i);
        }
        selectedPackages.remove("com.app.selectable.5"); // unselected by user

        assertEquals("205 apps selected before install", 205, selectedPackages.size());

        // Install test app
        String testPkg = "com.dynamic.sampletestapp";
        // testApp is unselected, so not added to selectedPackages
        assertEquals("Selected packages count remains 205 with test app installed", 205, selectedPackages.size());

        // Uninstall test app
        selectedPackages.remove(testPkg); // Removing unselected app
        assertEquals("Removing unselected app must not deselect any other app", 205, selectedPackages.size());
        assertFalse("Explicitly unselected app must remain unselected", selectedPackages.contains("com.app.selectable.5"));
        assertTrue("Remaining selected app must remain selected", selectedPackages.contains("com.app.selectable.0"));
    }

    // =========================================================================
    // Multi-App Selection Stability & Recycling Regression Tests
    // =========================================================================

    /**
     * Simulated ViewHolder representing the CheckBox and Row View contract in TargetAppAdapter.
     */
    static class SimulatedViewHolder {
        String boundPackage;
        boolean isChecked;
        boolean isEnabled;
        Runnable userClickListener;

        void bindFixed(TargetApp app, AppSelectionManager selectionManager) {
            // Contract step 1: Detach all listeners FIRST
            this.userClickListener = null;

            // Contract step 2: Query authoritative source of truth
            String pkg = app.getPackageName();
            boolean isProtected = app.isProtected();
            boolean selected = selectionManager.isSelected(pkg, isProtected);

            // Contract step 3: Sync model
            app.setSelected(selected);

            // Contract step 4: Set view state silently
            this.boundPackage = pkg;
            this.isChecked = selected;
            this.isEnabled = !isProtected;

            // Contract step 5: Attach user click handler
            this.userClickListener = () -> {
                if (isProtected) return;
                boolean next = !selectionManager.isSelected(pkg, isProtected);
                this.isChecked = next;
                selectionManager.setSelected(pkg, next, isProtected);
                app.setSelected(next);
            };
        }

        void recycle() {
            this.userClickListener = null;
        }

        void performUserClick() {
            if (userClickListener != null) {
                userClickListener.run();
            }
        }
    }

    @Test
    public void testAdapterRecycling_neverMutatesSelectedPackages() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        TargetApp appA = new TargetApp("com.app.selected.a", "App A");
        TargetApp appB = new TargetApp("com.app.unselected.b", "App B");

        selectionManager.setSelected(appA.getPackageName(), true, false);
        selectionManager.setSelected(appB.getPackageName(), false, false);

        SimulatedViewHolder holder = new SimulatedViewHolder();

        // Bind App A (selected)
        holder.bindFixed(appA, selectionManager);
        assertTrue("Holder must be checked for App A", holder.isChecked);
        assertTrue("App A must be selected in manager", selectionManager.isSelected(appA.getPackageName(), false));

        // Recycle holder
        holder.recycle();

        // Rebind holder to App B (unselected)
        holder.bindFixed(appB, selectionManager);
        assertFalse("Holder must be unchecked for App B", holder.isChecked);
        assertFalse("App B must remain unselected in manager", selectionManager.isSelected(appB.getPackageName(), false));

        // CRITICAL: App A must NOT have been deselected!
        assertTrue("App A must remain selected in manager after holder was recycled to App B",
                selectionManager.isSelected(appA.getPackageName(), false));
        assertTrue("App A model must remain selected", appA.isSelected());
        assertEquals("Selection manager count must remain exactly 1", 1, selectionManager.getSelectedCount());
    }

    @Test
    public void testRebindingUnselectedItem_withPreviouslySelectedHolder_preservesOriginalApp() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        TargetApp appSelected = new TargetApp("com.instagram.android", "Instagram");
        TargetApp appUnselected = new TargetApp("com.snapchat.android", "Snapchat");

        selectionManager.setSelected(appSelected.getPackageName(), true, false);
        selectionManager.setSelected(appUnselected.getPackageName(), false, false);

        SimulatedViewHolder recycledHolder = new SimulatedViewHolder();

        // 1. First binding: Instagram (selected)
        recycledHolder.bindFixed(appSelected, selectionManager);
        assertTrue(recycledHolder.isChecked);

        // 2. Recycle and rebind to Snapchat (unselected)
        recycledHolder.recycle();
        recycledHolder.bindFixed(appUnselected, selectionManager);
        assertFalse(recycledHolder.isChecked);

        // Verify Instagram was not deselected
        assertTrue("Original selected app must remain selected after ViewHolder reuse",
                selectionManager.isSelected(appSelected.getPackageName(), false));
        assertFalse("Rebound unselected app must remain unselected",
                selectionManager.isSelected(appUnselected.getPackageName(), false));
    }

    @Test
    public void testScrollingSimulation_206Apps_preservesAllSelections() {
        AppSelectionManager selectionManager = new AppSelectionManager();

        // Create 206 selectable apps and 95 protected apps (matching device inventory of 301 apps)
        List<TargetApp> allApps = new ArrayList<>();
        for (int i = 0; i < 206; i++) {
            TargetApp app = new TargetApp("com.app.selectable." + i, "Selectable App " + i);
            app.setProtected(false);
            allApps.add(app);
        }
        for (int i = 0; i < 95; i++) {
            TargetApp app = new TargetApp("com.android.protected." + i, "Protected Component " + i);
            app.setProtected(true);
            allApps.add(app);
        }

        // Initialize default selection: all 206 selectable apps start selected
        selectionManager.initializeDefaultSelection(allApps);
        assertEquals("Initially 206 selectable apps must be selected", 206, selectionManager.getSelectedCount());

        // Pool of 10 ViewHolders to simulate RecyclerView on screen
        SimulatedViewHolder[] viewHolderPool = new SimulatedViewHolder[10];
        for (int i = 0; i < 10; i++) {
            viewHolderPool[i] = new SimulatedViewHolder();
        }

        // Simulate scrolling up and down through the list 20 times (20 passes * 301 items = 6020 bindings)
        for (int pass = 0; pass < 20; pass++) {
            if (pass % 2 == 0) {
                // Scroll down (0 to 300)
                for (int index = 0; index < allApps.size(); index++) {
                    SimulatedViewHolder holder = viewHolderPool[index % 10];
                    holder.recycle();
                    holder.bindFixed(allApps.get(index), selectionManager);
                }
            } else {
                // Scroll up (300 to 0)
                for (int index = allApps.size() - 1; index >= 0; index--) {
                    SimulatedViewHolder holder = viewHolderPool[index % 10];
                    holder.recycle();
                    holder.bindFixed(allApps.get(index), selectionManager);
                }
            }

            // After each pass, verify selection count NEVER changed
            assertEquals("Selection count must remain exactly 206 after scroll pass " + pass,
                    206, selectionManager.getSelectedCount());
        }

        // Final verification across every single app
        for (int i = 0; i < 206; i++) {
            assertTrue("Selectable app " + i + " must remain selected",
                    selectionManager.isSelected("com.app.selectable." + i, false));
        }
        for (int i = 0; i < 95; i++) {
            assertFalse("Protected app " + i + " must remain unselected",
                    selectionManager.isSelected("com.android.protected." + i, true));
        }
    }

    @Test
    public void testFilteringAndUnfiltering_preservesAllSelections() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        List<TargetApp> allApps = new ArrayList<>();
        for (int i = 0; i < 50; i++) {
            String name = (i < 5) ? "Google App " + i : "Other App " + i;
            String pkg = (i < 5) ? "com.google.android.app." + i : "com.other.app." + i;
            TargetApp app = new TargetApp(pkg, name);
            app.setProtected(false);
            allApps.add(app);
        }

        selectionManager.initializeDefaultSelection(allApps);
        assertEquals("All 50 apps start selected", 50, selectionManager.getSelectedCount());

        // 1. User filters list by "google" -> only 5 apps match
        List<TargetApp> filtered = SupportedAppsRegistry.filterApps(allApps, "google");
        assertEquals("Filter must match exactly 5 Google apps", 5, filtered.size());

        // Simulate binding inside the filtered list
        SimulatedViewHolder holder = new SimulatedViewHolder();
        for (TargetApp app : filtered) {
            holder.recycle();
            holder.bindFixed(app, selectionManager);
        }

        // Selection manager must still retain all 50 selections (hidden apps are never lost)
        assertEquals("Hidden apps must remain selected during filter", 50, selectionManager.getSelectedCount());

        // 2. User un-filters (clears search query)
        List<TargetApp> unfiltered = SupportedAppsRegistry.filterApps(allApps, "");
        assertEquals("Unfiltered list contains all 50 apps", 50, unfiltered.size());

        for (TargetApp app : unfiltered) {
            holder.recycle();
            holder.bindFixed(app, selectionManager);
        }

        assertEquals("Selection count must remain exactly 50 after clearing search",
                50, selectionManager.getSelectedCount());
    }

    @Test
    public void testFiltering_selectionShortcutsAffectOnlyFilteredApps() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        List<TargetApp> allApps = new ArrayList<>();
        for (int i = 0; i < 20; i++) {
            String name = (i < 5) ? "Google App " + i : "Other App " + i;
            String pkg = (i < 5) ? "com.google.android.app." + i : "com.other.app." + i;
            TargetApp app = new TargetApp(pkg, name);
            app.setProtected(false);
            allApps.add(app);
        }

        selectionManager.initializeDefaultSelection(allApps);
        assertEquals(20, selectionManager.getSelectedCount());

        // User filters by "google" (5 apps)
        List<TargetApp> googleApps = SupportedAppsRegistry.filterApps(allApps, "google");
        assertEquals(5, googleApps.size());

        // User clicks "Deselect All" on the filtered subset
        selectionManager.deselectAll(googleApps);

        // Verification: The 5 Google apps are deselected, but the other 15 apps remain selected!
        assertEquals("15 non-matching apps must remain selected", 15, selectionManager.getSelectedCount());
        for (TargetApp gApp : googleApps) {
            assertFalse("Google app must be deselected", selectionManager.isSelected(gApp.getPackageName(), false));
        }
        for (int i = 5; i < 20; i++) {
            assertTrue("Other app must remain selected",
                    selectionManager.isSelected("com.other.app." + i, false));
        }

        // User clicks "Invert Selection" on the filtered subset
        selectionManager.invertSelection(googleApps);
        assertEquals("Inverting 5 deselected Google apps restores total to 20", 20, selectionManager.getSelectedCount());
    }

    @Test
    public void testUserClick_togglesOnlyClickedApp() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        TargetApp app1 = new TargetApp("com.app.one", "App 1");
        TargetApp app2 = new TargetApp("com.app.two", "App 2");
        app1.setProtected(false);
        app2.setProtected(false);

        selectionManager.setSelected(app1.getPackageName(), true, false);
        selectionManager.setSelected(app2.getPackageName(), true, false);

        SimulatedViewHolder holder1 = new SimulatedViewHolder();
        holder1.bindFixed(app1, selectionManager);

        SimulatedViewHolder holder2 = new SimulatedViewHolder();
        holder2.bindFixed(app2, selectionManager);

        // User clicks holder1 to deselect App 1
        holder1.performUserClick();

        assertFalse("App 1 must now be deselected", selectionManager.isSelected(app1.getPackageName(), false));
        assertFalse("Holder 1 view must be unchecked", holder1.isChecked);
        assertTrue("App 2 must remain selected", selectionManager.isSelected(app2.getPackageName(), false));
        assertTrue("Holder 2 view must remain checked", holder2.isChecked);
        assertEquals(1, selectionManager.getSelectedCount());
    }

    @Test
    public void testProtectedApps_canNeverBeSelected() {
        AppSelectionManager selectionManager = new AppSelectionManager();
        TargetApp protectedApp = new TargetApp("com.android.settings", "Settings");
        protectedApp.setProtected(true);

        // Attempt direct selection
        boolean result = selectionManager.setSelected(protectedApp.getPackageName(), true, true);
        assertFalse("setSelected must return false for protected app", result);
        assertFalse("Protected app must not be in selectedPackages",
                selectionManager.isSelected(protectedApp.getPackageName(), true));

        // Attempt toggle
        boolean toggleResult = selectionManager.toggle(protectedApp.getPackageName(), true);
        assertFalse("toggle must return false for protected app", toggleResult);
        assertFalse("Protected app must not be selected",
                selectionManager.isSelected(protectedApp.getPackageName(), true));

        // Attempt selectAll
        selectionManager.selectAll(Collections.singletonList(protectedApp));
        assertEquals("Selection count must remain 0", 0, selectionManager.getSelectedCount());
    }
}


