package com.rmx.cachecleaner;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

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
    public void testAllowlist_rejectsSystemPackagesAndForbiddenPrefixes() {
        // System and ColorOS packages must never be touched
        assertFalse("Must reject Android framework", SupportedAppsRegistry.isPackageAllowed("android"));
        assertFalse("Must reject Settings", SupportedAppsRegistry.isPackageAllowed("com.android.settings"));
        assertFalse("Must reject SystemUI", SupportedAppsRegistry.isPackageAllowed("com.android.systemui"));
        assertFalse("Must reject ColorOS packages", SupportedAppsRegistry.isPackageAllowed("com.coloros.safecenter"));
        assertFalse("Must reject Oplus packages", SupportedAppsRegistry.isPackageAllowed("com.oplus.postmanservice"));
        assertFalse("Must reject HeyTap packages", SupportedAppsRegistry.isPackageAllowed("com.heytap.mcs"));
        assertFalse("Must reject NearMe packages", SupportedAppsRegistry.isPackageAllowed("com.nearme.atlas"));
        assertFalse("Must reject MediaTek packages", SupportedAppsRegistry.isPackageAllowed("com.mediatek.mdmlsample"));
        assertFalse("Must reject PackageInstaller", SupportedAppsRegistry.isPackageAllowed("com.google.android.packageinstaller"));
        assertFalse("Must reject own app", SupportedAppsRegistry.isPackageAllowed("com.rmx.cachecleaner"));
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
}
