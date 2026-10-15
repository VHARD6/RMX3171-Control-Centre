package com.rmx.cachecleaner;

import android.content.Context;
import android.media.AudioManager;
import android.os.SystemClock;
import android.util.Log;

/**
 * Ultra-lightweight controller to request the native Android/ColorOS system volume interface.
 *
 * Uses public Android API AudioManager.adjustStreamVolume(STREAM_MUSIC, ADJUST_SAME, FLAG_SHOW_UI)
 * to reveal the native volume slider without altering the current volume level.
 * No custom UI, seekbars, broadcast receivers, or state tracking.
 */
public class EdgeVolumeController {

    private static final String TAG = "EdgeVolumeController";
    public static final long DEBOUNCE_MS = 800L;
    private static long lastTriggerTime = 0L;

    public interface AudioAdapter {
        void adjustStreamVolume(int streamType, int direction, int flags);
    }

    private static AudioAdapter customAdapter = null;

    public static void setCustomAdapter(AudioAdapter adapter) {
        customAdapter = adapter;
    }

    /**
     * Requests the native system volume UI without changing volume.
     * Debounced to prevent duplicate panel invocations from a single gesture.
     *
     * @return true if the request was dispatched, false if debounced or failed.
     */
    public static boolean triggerSystemVolumeUI(Context context) {
        long now = System.currentTimeMillis();
        if (now - lastTriggerTime < DEBOUNCE_MS) {
            return false;
        }
        lastTriggerTime = now;

        if (customAdapter != null) {
            customAdapter.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_SAME, AudioManager.FLAG_SHOW_UI);
            return true;
        }

        if (context == null) return false;

        try {
            AudioManager am = (AudioManager) context.getApplicationContext().getSystemService(Context.AUDIO_SERVICE);
            if (am != null) {
                am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_SAME, AudioManager.FLAG_SHOW_UI);
                return true;
            }
        } catch (Throwable t) {
            Log.w(TAG, "adjustStreamVolume failed for STREAM_MUSIC: " + t.getMessage());
            try {
                AudioManager am = (AudioManager) context.getApplicationContext().getSystemService(Context.AUDIO_SERVICE);
                if (am != null) {
                    am.adjustSuggestedStreamVolume(AudioManager.ADJUST_SAME, AudioManager.USE_DEFAULT_STREAM_TYPE, AudioManager.FLAG_SHOW_UI);
                    return true;
                }
            } catch (Throwable t2) {
                Log.e(TAG, "adjustSuggestedStreamVolume also failed: " + t2.getMessage());
            }
        }
        return false;
    }

    public static void resetDebounce() {
        lastTriggerTime = 0L;
    }
}
