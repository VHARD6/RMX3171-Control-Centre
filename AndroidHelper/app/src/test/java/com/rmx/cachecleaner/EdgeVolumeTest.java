package com.rmx.cachecleaner;

import android.media.AudioManager;

import org.junit.Before;
import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

/**
 * Unit tests for Edge Volume Control:
 * 1. Native volume UI request via AudioManager.adjustStreamVolume(STREAM_MUSIC, ADJUST_SAME, FLAG_SHOW_UI).
 * 2. Debounce protection against rapid repeated gesture triggers.
 * 3. Fallback and null safety checks.
 * 4. Preferences default constants and bounds validation.
 */
public class EdgeVolumeTest {

    private static class MockAudioAdapter implements EdgeVolumeController.AudioAdapter {
        int callCount = 0;
        int lastStreamType = -1;
        int lastDirection = -999;
        int lastFlags = -1;

        @Override
        public void adjustStreamVolume(int streamType, int direction, int flags) {
            callCount++;
            lastStreamType = streamType;
            lastDirection = direction;
            lastFlags = flags;
        }
    }

    private MockAudioAdapter mockAdapter;

    @Before
    public void setUp() {
        mockAdapter = new MockAudioAdapter();
        EdgeVolumeController.setCustomAdapter(mockAdapter);
        EdgeVolumeController.resetDebounce();
    }

    @Test
    public void testTriggerSystemVolumeUI_invokesWithCorrectFlags() {
        boolean dispatched = EdgeVolumeController.triggerSystemVolumeUI(null);

        assertTrue("Expected triggerSystemVolumeUI to return true", dispatched);
        assertEquals("Expected exactly one call to adjustStreamVolume", 1, mockAdapter.callCount);
        assertEquals("Stream must be STREAM_MUSIC", AudioManager.STREAM_MUSIC, mockAdapter.lastStreamType);
        assertEquals("Direction must be ADJUST_SAME to preserve current volume", AudioManager.ADJUST_SAME, mockAdapter.lastDirection);
        assertEquals("Flags must contain FLAG_SHOW_UI to invoke native system panel", AudioManager.FLAG_SHOW_UI, mockAdapter.lastFlags);
    }

    @Test
    public void testTriggerSystemVolumeUI_debounced() {
        boolean first = EdgeVolumeController.triggerSystemVolumeUI(null);
        assertTrue("First trigger must succeed", first);
        assertEquals(1, mockAdapter.callCount);

        // Immediate subsequent call must be debounced
        boolean second = EdgeVolumeController.triggerSystemVolumeUI(null);
        assertFalse("Second trigger within debounce window must return false", second);
        assertEquals("Debounced call must not invoke audio manager", 1, mockAdapter.callCount);

        // Resetting debounce allows new invocation
        EdgeVolumeController.resetDebounce();
        boolean third = EdgeVolumeController.triggerSystemVolumeUI(null);
        assertTrue("Trigger after resetDebounce must succeed", third);
        assertEquals(2, mockAdapter.callCount);
    }

    @Test
    public void testPreferencesDefaultsAndNullSafety() {
        assertFalse("Null context should report disabled", EdgeVolumePreferences.isEnabled(null));
        assertEquals(EdgeVolumePreferences.DEFAULT_Y_RATIO, EdgeVolumePreferences.getHandleYRatio(null), 0.001f);
        assertEquals(EdgeVolumePreferences.DEFAULT_OPACITY, EdgeVolumePreferences.getHandleOpacity(null), 0.001f);

        // Verify bounds constants
        assertEquals(0.50f, EdgeVolumePreferences.DEFAULT_Y_RATIO, 0.001f);
        assertEquals(0.15f, EdgeVolumePreferences.DEFAULT_OPACITY, 0.001f);
        assertEquals(0.08f, EdgeVolumePreferences.MIN_OPACITY, 0.001f);
        assertEquals(0.60f, EdgeVolumePreferences.MAX_OPACITY, 0.001f);
    }
}
