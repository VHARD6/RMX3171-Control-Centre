package com.rmx.cachecleaner;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Thread-safe repository for persisting and retrieving cache cleaning operation records.
 *
 * Guarantees:
 * 1. Bounded capacity: Retains the latest 50 records (newest-first).
 * 2. Low storage overhead: Pure lightweight JSON array stored in SharedPreferences.
 * 3. Atomic writes and in-memory cache for zero disk latency on UI queries.
 * 4. Pluggable persistence delegate for 100% headless unit testing.
 */
public final class CleaningHistoryRepository {

    private static final String TAG = "RMX_HistoryRepo";
    public static final int MAX_ENTRIES = 50;
    private static final String PREFS_NAME = "rmx_cleaning_history_prefs";
    private static final String KEY_HISTORY_JSON = "history_entries_json";

    public interface PersistenceDelegate {
        @Nullable
        String loadRawJson();
        void saveRawJson(@Nullable String rawJson);
    }

    private static CleaningHistoryRepository sInstance;

    private final List<CleaningHistoryRecord> records = new ArrayList<>();
    private PersistenceDelegate persistenceDelegate;
    private boolean initialized = false;

    private CleaningHistoryRepository() {}

    public static synchronized CleaningHistoryRepository getInstance(@Nullable Context context) {
        if (sInstance == null) {
            sInstance = new CleaningHistoryRepository();
        }
        if (!sInstance.initialized && context != null) {
            sInstance.initDefaultPersistence(context.getApplicationContext());
        }
        return sInstance;
    }

    /**
     * For unit test setup and teardown.
     */
    public static synchronized void resetInstanceForTesting() {
        sInstance = null;
    }

    public synchronized void setPersistenceDelegateForTesting(@Nullable PersistenceDelegate delegate) {
        this.persistenceDelegate = delegate;
        this.initialized = true;
        this.records.clear();
        loadRecords();
    }

    private void initDefaultPersistence(final Context appContext) {
        this.persistenceDelegate = new PersistenceDelegate() {
            @Override
            public String loadRawJson() {
                try {
                    SharedPreferences prefs = appContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
                    return prefs.getString(KEY_HISTORY_JSON, null);
                } catch (Throwable t) {
                    Log.e(TAG, "Error loading history from SharedPreferences", t);
                    return null;
                }
            }

            @Override
            public void saveRawJson(String rawJson) {
                try {
                    SharedPreferences prefs = appContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
                    prefs.edit().putString(KEY_HISTORY_JSON, rawJson).apply();
                } catch (Throwable t) {
                    Log.e(TAG, "Error saving history to SharedPreferences", t);
                }
            }
        };
        this.initialized = true;
        loadRecords();
    }

    private synchronized void loadRecords() {
        records.clear();
        if (persistenceDelegate == null) return;

        String raw = persistenceDelegate.loadRawJson();
        if (raw == null || raw.trim().isEmpty()) return;

        try {
            List<String> rawObjects = parseJsonArrayOfObjects(raw);
            for (String rawObj : rawObjects) {
                CleaningHistoryRecord record = CleaningHistoryRecord.fromJsonString(rawObj);
                records.add(record);
                if (records.size() >= MAX_ENTRIES) break;
            }
        } catch (Throwable t) {
            Log.e(TAG, "Failed to parse stored cleaning history JSON", t);
        }
    }

    private synchronized void persistRecords() {
        if (persistenceDelegate == null) return;

        try {
            StringBuilder sb = new StringBuilder();
            sb.append("[");
            for (int i = 0; i < records.size(); i++) {
                if (i > 0) sb.append(",");
                sb.append(records.get(i).toJsonString());
            }
            sb.append("]");
            persistenceDelegate.saveRawJson(sb.toString());
        } catch (Throwable t) {
            Log.e(TAG, "Failed to serialize cleaning history for persistence", t);
        }
    }

    public static List<String> parseJsonArrayOfObjects(String json) {
        List<String> list = new ArrayList<>();
        if (json == null) return list;
        json = json.trim();
        if (!json.startsWith("[") || !json.endsWith("]")) return list;

        int depth = 0;
        int objStart = -1;
        boolean inString = false;
        for (int i = 0; i < json.length(); i++) {
            char c = json.charAt(i);
            if (c == '"') {
                int slashes = 0;
                int k = i - 1;
                while (k >= 0 && json.charAt(k) == '\\') {
                    slashes++;
                    k--;
                }
                if (slashes % 2 == 0) {
                    inString = !inString;
                }
            } else if (!inString) {
                if (c == '{') {
                    if (depth == 0) {
                        objStart = i;
                    }
                    depth++;
                } else if (c == '}') {
                    depth--;
                    if (depth == 0 && objStart != -1) {
                        list.add(json.substring(objStart, i + 1));
                        objStart = -1;
                    }
                }
            }
        }
        return list;
    }

    /**
     * Records a new cleaning run.
     * Inserts at index 0 (newest-first) and bounds history to MAX_ENTRIES (50).
     */
    public synchronized void addRecord(@NonNull CleaningHistoryRecord record) {
        records.add(0, record);

        // Cap to MAX_ENTRIES
        while (records.size() > MAX_ENTRIES) {
            records.remove(records.size() - 1);
        }

        persistRecords();
        Log.i(TAG, "Saved cleaning record: " + record.getCleanType() + " outcome=" + record.getOutcome()
                + " delta=" + record.getFormattedStorageDelta() + " total_stored=" + records.size());
    }

    /**
     * Returns an unmodifiable list of all history entries, ordered newest-first.
     */
    @NonNull
    public synchronized List<CleaningHistoryRecord> getRecords() {
        return Collections.unmodifiableList(new ArrayList<>(records));
    }

    /**
     * Returns the most recent cleaning record, or null if history is empty.
     */
    @Nullable
    public synchronized CleaningHistoryRecord getLatestRecord() {
        if (records.isEmpty()) return null;
        return records.get(0);
    }

    /**
     * Returns the total count of stored records.
     */
    public synchronized int getRecordCount() {
        return records.size();
    }

    /**
     * Clears all stored cleaning history.
     */
    public synchronized void clearHistory() {
        records.clear();
        if (persistenceDelegate != null) {
            persistenceDelegate.saveRawJson(null);
        }
        Log.i(TAG, "Cleaning history cleared.");
    }
}
