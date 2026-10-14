package com.rmx.cachecleaner;

import android.content.Context;
import android.content.SharedPreferences;

/**
 * Manages persisted configuration and user preferences for the Edge Volume Control feature.
 *
 * Persists:
 * 1. Opt-in enabled state (default: false)
 * 2. Handle vertical position ratio (0.0 to 1.0, default: 0.5f)
 * 3. Handle idle opacity (0.10f to 0.50f, default: 0.15f)
 * 4. Last non-zero media volume for media mute/unmute restoration
 */
public class EdgeVolumePreferences {

    public static final String PREF_NAME = "rmx_edge_volume_prefs";

    public static final String KEY_ENABLED = "edge_volume_enabled";
    public static final String KEY_HANDLE_Y_RATIO = "handle_y_ratio";
    public static final String KEY_HANDLE_OPACITY = "handle_opacity";
    public static final String KEY_LAST_MEDIA_VOLUME = "last_media_volume";

    public static final float DEFAULT_Y_RATIO = 0.50f;
    public static final float DEFAULT_OPACITY = 0.15f;
    public static final float MIN_OPACITY = 0.08f;
    public static final float MAX_OPACITY = 0.60f;

    private static SharedPreferences getPrefs(Context context) {
        return context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE);
    }

    public static boolean isEnabled(Context context) {
        if (context == null) return false;
        return getPrefs(context).getBoolean(KEY_ENABLED, false);
    }

    public static void setEnabled(Context context, boolean enabled) {
        if (context == null) return;
        getPrefs(context).edit().putBoolean(KEY_ENABLED, enabled).apply();
    }

    public static float getHandleYRatio(Context context) {
        if (context == null) return DEFAULT_Y_RATIO;
        return getPrefs(context).getFloat(KEY_HANDLE_Y_RATIO, DEFAULT_Y_RATIO);
    }

    public static void setHandleYRatio(Context context, float ratio) {
        if (context == null) return;
        float clamped = Math.max(0.05f, Math.min(0.95f, ratio));
        getPrefs(context).edit().putFloat(KEY_HANDLE_Y_RATIO, clamped).apply();
    }

    public static float getHandleOpacity(Context context) {
        if (context == null) return DEFAULT_OPACITY;
        return getPrefs(context).getFloat(KEY_HANDLE_OPACITY, DEFAULT_OPACITY);
    }

    public static void setHandleOpacity(Context context, float opacity) {
        if (context == null) return;
        float clamped = Math.max(MIN_OPACITY, Math.min(MAX_OPACITY, opacity));
        getPrefs(context).edit().putFloat(KEY_HANDLE_OPACITY, clamped).apply();
    }

    public static void resetPosition(Context context) {
        if (context == null) return;
        getPrefs(context).edit()
                .putFloat(KEY_HANDLE_Y_RATIO, DEFAULT_Y_RATIO)
                .apply();
    }

    public static int getLastMediaVolume(Context context, int defaultVolume) {
        if (context == null) return defaultVolume;
        return getPrefs(context).getInt(KEY_LAST_MEDIA_VOLUME, defaultVolume);
    }

    public static void setLastMediaVolume(Context context, int volume) {
        if (context == null) return;
        getPrefs(context).edit().putInt(KEY_LAST_MEDIA_VOLUME, volume).apply();
    }
}
