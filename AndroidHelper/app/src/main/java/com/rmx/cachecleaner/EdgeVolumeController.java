package com.rmx.cachecleaner;

import android.content.Context;
import android.media.AudioManager;
import android.os.Build;
import android.util.Log;

/**
 * Core controller and AudioManager abstraction for the Edge Volume Control feature.
 *
 * Responsibilities:
 * 1. Querying current, minimum, and maximum volume for Media, Ring, and Alarm streams.
 * 2. Real-time volume stream modifications without showing intrusive system HUD.
 * 3. Safe Media mute toggle with volume restoration, ensuring Alarm is never touched.
 * 4. Graceful handling of Do Not Disturb (DND) / ZenMode SecurityExceptions on Android 11.
 * 5. Injectable AudioAdapter interface for deterministic JUnit testing.
 */
public class EdgeVolumeController {

    private static final String TAG = "EdgeVolumeController";

    public interface AudioAdapter {
        int getStreamVolume(int streamType);
        int getStreamMinVolume(int streamType);
        int getStreamMaxVolume(int streamType);
        void setStreamVolume(int streamType, int index, int flags);
    }

    private final Context context;
    private final AudioAdapter audioAdapter;

    public EdgeVolumeController(Context context) {
        this(context, null);
    }

    public EdgeVolumeController(Context context, AudioAdapter customAdapter) {
        this.context = (context != null) ? context.getApplicationContext() : null;
        if (customAdapter != null) {
            this.audioAdapter = customAdapter;
        } else if (context != null) {
            final AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            this.audioAdapter = new AudioAdapter() {
                @Override
                public int getStreamVolume(int streamType) {
                    return (am != null) ? am.getStreamVolume(streamType) : 0;
                }

                @Override
                public int getStreamMinVolume(int streamType) {
                    if (am == null) return 0;
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                        try {
                            return am.getStreamMinVolume(streamType);
                        } catch (Throwable t) {
                            return 0;
                        }
                    }
                    return 0;
                }

                @Override
                public int getStreamMaxVolume(int streamType) {
                    return (am != null) ? am.getStreamMaxVolume(streamType) : 100;
                }

                @Override
                public void setStreamVolume(int streamType, int index, int flags) {
                    if (am != null) {
                        am.setStreamVolume(streamType, index, flags);
                    }
                }
            };
        } else {
            this.audioAdapter = null;
        }
    }

    public int getVolume(int streamType) {
        if (audioAdapter == null) return 0;
        try {
            return audioAdapter.getStreamVolume(streamType);
        } catch (Throwable t) {
            Log.w(TAG, "Error reading volume for stream " + streamType + ": " + t.getMessage());
            return 0;
        }
    }

    public int getMinVolume(int streamType) {
        if (audioAdapter == null) return 0;
        try {
            return audioAdapter.getStreamMinVolume(streamType);
        } catch (Throwable t) {
            return 0;
        }
    }

    public int getMaxVolume(int streamType) {
        if (audioAdapter == null) return 100;
        try {
            int max = audioAdapter.getStreamMaxVolume(streamType);
            return (max > 0) ? max : 100;
        } catch (Throwable t) {
            return 100;
        }
    }

    /**
     * Safely updates volume for the specified stream type.
     * Catches SecurityExceptions on Android 11 if system DND policy blocks ring volume adjustment.
     *
     * @return true if volume was set successfully, false if blocked or failed.
     */
    public boolean setVolume(int streamType, int volume) {
        if (audioAdapter == null) return false;
        int min = getMinVolume(streamType);
        int max = getMaxVolume(streamType);
        int clamped = Math.max(min, Math.min(max, volume));

        try {
            audioAdapter.setStreamVolume(streamType, clamped, 0);

            // If setting media volume > min, record it as last non-zero media volume
            if (streamType == AudioManager.STREAM_MUSIC && clamped > min && context != null) {
                EdgeVolumePreferences.setLastMediaVolume(context, clamped);
            }
            return true;
        } catch (SecurityException se) {
            Log.w(TAG, "SecurityException adjusting stream " + streamType + " (likely DND/Zen mode): " + se.getMessage());
            return false;
        } catch (Throwable t) {
            Log.e(TAG, "Unexpected error adjusting stream " + streamType + ": " + t.getMessage());
            return false;
        }
    }

    public boolean isMediaMuted() {
        int current = getVolume(AudioManager.STREAM_MUSIC);
        int min = getMinVolume(AudioManager.STREAM_MUSIC);
        return current <= min;
    }

    /**
     * Toggles media mute. If currently muted, restores to last known non-zero volume (or 50% max).
     * If currently unmuted, saves volume and sets to minimum.
     *
     * Strictly touches AudioManager.STREAM_MUSIC only.
     */
    public boolean toggleMediaMute() {
        int min = getMinVolume(AudioManager.STREAM_MUSIC);
        int max = getMaxVolume(AudioManager.STREAM_MUSIC);
        int current = getVolume(AudioManager.STREAM_MUSIC);

        if (current > min) {
            // Mute media
            if (context != null) {
                EdgeVolumePreferences.setLastMediaVolume(context, current);
            }
            return setVolume(AudioManager.STREAM_MUSIC, min);
        } else {
            // Unmute media
            int defaultRestore = Math.max(1, max / 2);
            int restoreVol = (context != null)
                    ? EdgeVolumePreferences.getLastMediaVolume(context, defaultRestore)
                    : defaultRestore;
            if (restoreVol <= min) {
                restoreVol = defaultRestore;
            }
            return setVolume(AudioManager.STREAM_MUSIC, restoreVol);
        }
    }

    /**
     * Computes the display percentage for a stream (0 - 100%).
     */
    public int getVolumePercentage(int streamType) {
        int min = getMinVolume(streamType);
        int max = getMaxVolume(streamType);
        int current = getVolume(streamType);

        if (max <= min) return 0;
        int pct = Math.round(((float) (current - min) / (max - min)) * 100f);
        return Math.max(0, Math.min(100, pct));
    }
}
