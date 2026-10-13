package com.rmx.cachecleaner;

import org.junit.Before;
import org.junit.Test;

import static org.junit.Assert.*;

/**
 * Unit tests for PowerMenuController:
 * Verifies all 9 state transitions, safety guards, and debouncing without requiring a live Android device.
 */
public class PowerMenuTest {

    private static class MockActionDelegate implements PowerMenuController.ActionDelegate {
        boolean connected = true;
        boolean enabledInSettings = true;
        boolean cleaningActive = false;
        boolean systemActionSuccess = true;

        @Override
        public boolean isServiceConnected() {
            return connected;
        }

        @Override
        public boolean isEnabledInSettings(android.content.Context context) {
            return enabledInSettings;
        }

        @Override
        public boolean isCleaningInProgress() {
            return cleaningActive;
        }

        @Override
        public boolean performPowerDialog() {
            return systemActionSuccess;
        }
    }

    private MockActionDelegate mockDelegate;

    @Before
    public void setUp() {
        mockDelegate = new MockActionDelegate();
        PowerMenuController.setDelegateForTesting(mockDelegate);
        PowerMenuController.resetThrottleForTesting();
    }

    @Test
    public void testGetStatus_whenConnected_returnsReady() {
        mockDelegate.connected = true;
        mockDelegate.enabledInSettings = true;

        assertEquals(PowerMenuController.Status.READY, PowerMenuController.getStatus(null));
    }

    @Test
    public void testGetStatus_whenDisconnectedButEnabledInSettings_returnsDisconnected() {
        mockDelegate.connected = false;
        mockDelegate.enabledInSettings = true;

        assertEquals(PowerMenuController.Status.SERVICE_DISCONNECTED, PowerMenuController.getStatus(null));
    }

    @Test
    public void testGetStatus_whenDisabledInSettings_returnsDisabled() {
        mockDelegate.connected = false;
        mockDelegate.enabledInSettings = false;

        assertEquals(PowerMenuController.Status.SERVICE_DISABLED, PowerMenuController.getStatus(null));
    }

    @Test
    public void testTriggerPowerMenu_successPath_returnsSuccess() {
        mockDelegate.connected = true;
        mockDelegate.systemActionSuccess = true;

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.SUCCESS, result);
    }

    @Test
    public void testTriggerPowerMenu_whenSystemRejects_returnsFailedSystemRejected() {
        mockDelegate.connected = true;
        mockDelegate.systemActionSuccess = false;

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.FAILED_SYSTEM_REJECTED, result);
    }

    @Test
    public void testTriggerPowerMenu_whenServiceDisabled_returnsServiceNotEnabled() {
        mockDelegate.connected = false;
        mockDelegate.enabledInSettings = false;

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.SERVICE_NOT_ENABLED, result);
    }

    @Test
    public void testTriggerPowerMenu_whenServiceDisconnected_returnsServiceNotConnected() {
        mockDelegate.connected = false;
        mockDelegate.enabledInSettings = true;

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.SERVICE_NOT_CONNECTED, result);
    }

    @Test
    public void testTriggerPowerMenu_whenCleaningActive_guardsBatchAndReturnsCleaningInProgress() {
        mockDelegate.connected = true;
        mockDelegate.cleaningActive = true;

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.CLEANING_IN_PROGRESS, result);
    }

    @Test
    public void testTriggerPowerMenu_rapidTaps_areThrottled() {
        mockDelegate.connected = true;
        mockDelegate.systemActionSuccess = true;

        // First tap succeeds
        PowerMenuController.Result first = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.SUCCESS, first);

        // Immediate second tap is throttled
        PowerMenuController.Result second = PowerMenuController.triggerPowerMenu(null);
        assertEquals(PowerMenuController.Result.THROTTLED, second);
    }
}
