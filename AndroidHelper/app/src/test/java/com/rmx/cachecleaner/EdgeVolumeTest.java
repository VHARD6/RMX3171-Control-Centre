package com.rmx.cachecleaner;

import android.media.AudioManager;

import org.junit.Before;
import org.junit.Test;

import java.util.HashMap;
import java.util.Map;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

/**
 * Unit tests for Edge Volume Control components:
 * 1. EdgeVolumeController audio abstraction, volume bounds, and stream isolation.
 * 2. Media mute toggle and restoration logic.
 * 3. Strict guard ensuring Alarm volume is NEVER touched by media mute.
 * 4. Graceful handling of SecurityException (DND / Zen mode restrictions).
 * 5. Percentage calculation accuracy.
 */
public class EdgeVolumeTest {

    private static class MockAudioAdapter implements EdgeVolumeController.AudioAdapter {
        final Map<Integer, Integer> volumes = new HashMap<>();
        final Map<Integer, Integer> minVolumes = new HashMap<>();
        final Map<Integer, Integer> maxVolumes = new HashMap<>();
        boolean throwSecurityExceptionOnRing = false;

        MockAudioAdapter() {
            // STREAM_MUSIC (Media): 0 to 15, initial 8
            minVolumes.put(AudioManager.STREAM_MUSIC, 0);
            maxVolumes.put(AudioManager.STREAM_MUSIC, 15);
            volumes.put(AudioManager.STREAM_MUSIC, 8);

            // STREAM_RING (Ring): 0 to 7, initial 5
            minVolumes.put(AudioManager.STREAM_RING, 0);
            maxVolumes.put(AudioManager.STREAM_RING, 7);
            volumes.put(AudioManager.STREAM_RING, 5);

            // STREAM_ALARM (Alarm): 1 to 7, initial 6
            minVolumes.put(AudioManager.STREAM_ALARM, 1);
            maxVolumes.put(AudioManager.STREAM_ALARM, 7);
            volumes.put(AudioManager.STREAM_ALARM, 6);
        }

        @Override
        public int getStreamVolume(int streamType) {
            return volumes.getOrDefault(streamType, 0);
        }

        @Override
        public int getStreamMinVolume(int streamType) {
            return minVolumes.getOrDefault(streamType, 0);
        }

        @Override
        public int getStreamMaxVolume(int streamType) {
            return maxVolumes.getOrDefault(streamType, 100);
        }

        @Override
        public void setStreamVolume(int streamType, int index, int flags) {
            if (throwSecurityExceptionOnRing && streamType == AudioManager.STREAM_RING) {
                throw new SecurityException("Not allowed by DND policy on Android 11");
            }
            volumes.put(streamType, index);
        }
    }

    private MockAudioAdapter mockAdapter;
    private EdgeVolumeController controller;

    @Before
    public void setUp() {
        mockAdapter = new MockAudioAdapter();
        controller = new EdgeVolumeController(null, mockAdapter);
    }

    @Test
    public void testInitialStreamVolumes() {
        assertEquals(8, controller.getVolume(AudioManager.STREAM_MUSIC));
        assertEquals(5, controller.getVolume(AudioManager.STREAM_RING));
        assertEquals(6, controller.getVolume(AudioManager.STREAM_ALARM));

        assertEquals(0, controller.getMinVolume(AudioManager.STREAM_MUSIC));
        assertEquals(15, controller.getMaxVolume(AudioManager.STREAM_MUSIC));

        assertEquals(1, controller.getMinVolume(AudioManager.STREAM_ALARM));
        assertEquals(7, controller.getMaxVolume(AudioManager.STREAM_ALARM));
    }

    @Test
    public void testSetVolumeClamping() {
        // Set below min
        boolean resMin = controller.setVolume(AudioManager.STREAM_ALARM, 0);
        assertTrue(resMin);
        assertEquals(1, controller.getVolume(AudioManager.STREAM_ALARM)); // Clamped to min (1)

        // Set above max
        boolean resMax = controller.setVolume(AudioManager.STREAM_MUSIC, 25);
        assertTrue(resMax);
        assertEquals(15, controller.getVolume(AudioManager.STREAM_MUSIC)); // Clamped to max (15)

        // Set normal volume
        boolean resNormal = controller.setVolume(AudioManager.STREAM_RING, 3);
        assertTrue(resNormal);
        assertEquals(3, controller.getVolume(AudioManager.STREAM_RING));
    }

    @Test
    public void testPercentageCalculation() {
        // Media: range 0..15
        mockAdapter.volumes.put(AudioManager.STREAM_MUSIC, 0);
        assertEquals(0, controller.getVolumePercentage(AudioManager.STREAM_MUSIC));

        mockAdapter.volumes.put(AudioManager.STREAM_MUSIC, 15);
        assertEquals(100, controller.getVolumePercentage(AudioManager.STREAM_MUSIC));

        mockAdapter.volumes.put(AudioManager.STREAM_MUSIC, 8);
        // (8 - 0) / 15 * 100 = 53.33% -> 53%
        assertEquals(53, controller.getVolumePercentage(AudioManager.STREAM_MUSIC));

        // Alarm: range 1..7 (delta = 6)
        mockAdapter.volumes.put(AudioManager.STREAM_ALARM, 1);
        assertEquals(0, controller.getVolumePercentage(AudioManager.STREAM_ALARM));

        mockAdapter.volumes.put(AudioManager.STREAM_ALARM, 4);
        // (4 - 1) / 6 * 100 = 50%
        assertEquals(50, controller.getVolumePercentage(AudioManager.STREAM_ALARM));

        mockAdapter.volumes.put(AudioManager.STREAM_ALARM, 7);
        assertEquals(100, controller.getVolumePercentage(AudioManager.STREAM_ALARM));
    }

    @Test
    public void testMediaMuteToggleAndRestoration() {
        // Initially 8 -> not muted
        assertFalse(controller.isMediaMuted());

        // Toggle mute -> should become 0
        boolean muted = controller.toggleMediaMute();
        assertTrue(muted);
        assertEquals(0, controller.getVolume(AudioManager.STREAM_MUSIC));
        assertTrue(controller.isMediaMuted());

        // Toggle again -> should restore
        boolean unmuted = controller.toggleMediaMute();
        assertTrue(unmuted);
        assertTrue(controller.getVolume(AudioManager.STREAM_MUSIC) > 0);
        assertFalse(controller.isMediaMuted());
    }

    @Test
    public void testStrictAlarmIsolation() {
        int initialAlarm = controller.getVolume(AudioManager.STREAM_ALARM);
        assertEquals(6, initialAlarm);

        // Mute media
        controller.toggleMediaMute();
        assertEquals(0, controller.getVolume(AudioManager.STREAM_MUSIC));

        // Alarm MUST remain completely unchanged!
        assertEquals(initialAlarm, controller.getVolume(AudioManager.STREAM_ALARM));

        // Set media to max
        controller.setVolume(AudioManager.STREAM_MUSIC, 15);
        assertEquals(initialAlarm, controller.getVolume(AudioManager.STREAM_ALARM));

        // Set ring volume
        controller.setVolume(AudioManager.STREAM_RING, 1);
        assertEquals(initialAlarm, controller.getVolume(AudioManager.STREAM_ALARM));
    }

    @Test
    public void testSecurityExceptionHandlingOnDND() {
        mockAdapter.throwSecurityExceptionOnRing = true;

        // Modifying ring volume should fail safely without crashing
        boolean success = controller.setVolume(AudioManager.STREAM_RING, 0);
        assertFalse(success);

        // Media and Alarm streams should still work unaffected
        boolean mediaSuccess = controller.setVolume(AudioManager.STREAM_MUSIC, 10);
        assertTrue(mediaSuccess);
        assertEquals(10, controller.getVolume(AudioManager.STREAM_MUSIC));
    }

    @Test
    public void testPreferencesDefaultsAndClamping() {
        assertEquals(0.50f, EdgeVolumePreferences.DEFAULT_Y_RATIO, 0.001f);
        assertEquals(0.15f, EdgeVolumePreferences.DEFAULT_OPACITY, 0.001f);

        // Opacity min/max boundaries
        assertEquals(0.08f, EdgeVolumePreferences.MIN_OPACITY, 0.001f);
        assertEquals(0.60f, EdgeVolumePreferences.MAX_OPACITY, 0.001f);
    }
}
