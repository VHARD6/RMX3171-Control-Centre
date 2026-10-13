package com.rmx.cachecleaner;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

/**
 * Immutable record of a cache cleaning operation in RMX Device Hub.
 *
 * Stored attributes include:
 * - Unique ID & epoch timestamp
 * - Cleanup type (Quick Clean All, Guarded Deep Clean, Test Pilot)
 * - Execution outcome (Success, Partial, Failed, Cancelled)
 * - Duration in ms
 * - Before & After internal storage measurements (via StatFs)
 * - Target application breakdown (targeted, cleared, already-clean, failed, cancelled)
 * - Verifiable summary and scope notes
 *
 * Implements self-contained, zero-dependency JSON serialization so that
 * local JVM unit tests run without being affected by Android SDK stubbing.
 */
public final class CleaningHistoryRecord {

    public enum CleanType {
        QUICK_CLEAN("Quick Clean All (Shizuku)"),
        DEEP_CLEAN("Guarded Deep Clean (Accessibility)"),
        PILOT_CLEAN("Test Pilot (Instagram)");

        private final String displayName;

        CleanType(String displayName) {
            this.displayName = displayName;
        }

        public String getDisplayName() {
            return displayName;
        }
    }

    public enum Outcome {
        SUCCESS("SUCCESS"),
        PARTIAL("PARTIAL"),
        FAILED("FAILED"),
        CANCELLED("CANCELLED");

        private final String displayName;

        Outcome(String displayName) {
            this.displayName = displayName;
        }

        public String getDisplayName() {
            return displayName;
        }
    }

    private final String id;
    private final long timestamp;
    private final CleanType cleanType;
    private final Outcome outcome;
    private final long durationMs;
    private final long freeBytesBefore;
    private final long freeBytesAfter;
    private final long bytesDelta;
    private final boolean storageMeasured;
    private final int appsTargeted;
    private final int appsCleared;
    private final int appsAlreadyClean;
    private final int appsFailed;
    private final int appsCancelled;
    private final String summaryMessage;
    private final String detailNote;

    public CleaningHistoryRecord(@NonNull String id,
                                 long timestamp,
                                 @NonNull CleanType cleanType,
                                 @NonNull Outcome outcome,
                                 long durationMs,
                                 long freeBytesBefore,
                                 long freeBytesAfter,
                                 int appsTargeted,
                                 int appsCleared,
                                 int appsAlreadyClean,
                                 int appsFailed,
                                 int appsCancelled,
                                 @Nullable String summaryMessage,
                                 @Nullable String detailNote) {
        this.id = id;
        this.timestamp = timestamp;
        this.cleanType = cleanType;
        this.outcome = outcome;
        this.durationMs = Math.max(0, durationMs);
        this.freeBytesBefore = freeBytesBefore;
        this.freeBytesAfter = freeBytesAfter;

        if (freeBytesBefore > 0 && freeBytesAfter > 0) {
            this.storageMeasured = true;
            this.bytesDelta = freeBytesAfter - freeBytesBefore;
        } else {
            this.storageMeasured = false;
            this.bytesDelta = 0;
        }

        this.appsTargeted = Math.max(0, appsTargeted);
        this.appsCleared = Math.max(0, appsCleared);
        this.appsAlreadyClean = Math.max(0, appsAlreadyClean);
        this.appsFailed = Math.max(0, appsFailed);
        this.appsCancelled = Math.max(0, appsCancelled);
        this.summaryMessage = (summaryMessage != null) ? summaryMessage : "";
        this.detailNote = (detailNote != null) ? detailNote : "";
    }

    public static CleaningHistoryRecord createQuickClean(Outcome outcome,
                                                         long durationMs,
                                                         long freeBytesBefore,
                                                         long freeBytesAfter,
                                                         String summaryMessage) {
        return new CleaningHistoryRecord(
                UUID.randomUUID().toString(),
                System.currentTimeMillis(),
                CleanType.QUICK_CLEAN,
                outcome,
                durationMs,
                freeBytesBefore,
                freeBytesAfter,
                0, 0, 0, 0, 0,
                summaryMessage,
                "Executed Android system cache trimming command (pm trim-caches 100G) via Shizuku shell."
        );
    }

    public static CleaningHistoryRecord createDeepClean(Outcome outcome,
                                                        long durationMs,
                                                        long freeBytesBefore,
                                                        long freeBytesAfter,
                                                        int appsTargeted,
                                                        int appsCleared,
                                                        int appsAlreadyClean,
                                                        int appsFailed,
                                                        int appsCancelled,
                                                        String summaryMessage) {
        return new CleaningHistoryRecord(
                UUID.randomUUID().toString(),
                System.currentTimeMillis(),
                CleanType.DEEP_CLEAN,
                outcome,
                durationMs,
                freeBytesBefore,
                freeBytesAfter,
                appsTargeted,
                appsCleared,
                appsAlreadyClean,
                appsFailed,
                appsCancelled,
                summaryMessage,
                "Guarded private app cache cleaning via Settings Accessibility automation."
        );
    }

    /**
     * Determines the honest overall Outcome for a batch cleaning operation based on
     * summary message and individual app execution counts.
     */
    public static Outcome evaluateBatchOutcome(String summary, int cleared, int alreadyClean, int failed, int cancelled) {
        if (summary != null && summary.startsWith("Batch cancelled")) {
            return Outcome.CANCELLED;
        } else if (cleared == 0 && alreadyClean == 0 && (failed > 0 || (summary != null && summary.startsWith("Batch stopped")))) {
            return Outcome.FAILED;
        } else if (failed > 0 || cancelled > 0 || (summary != null && (summary.startsWith("Batch stopped") || summary.contains("partial issues")))) {
            return Outcome.PARTIAL;
        } else {
            return Outcome.SUCCESS;
        }
    }

    public String getId() {
        return id;
    }

    public long getTimestamp() {
        return timestamp;
    }

    public CleanType getCleanType() {
        return cleanType;
    }

    public Outcome getOutcome() {
        return outcome;
    }

    public long getDurationMs() {
        return durationMs;
    }

    public long getFreeBytesBefore() {
        return freeBytesBefore;
    }

    public long getFreeBytesAfter() {
        return freeBytesAfter;
    }

    public long getBytesDelta() {
        return bytesDelta;
    }

    public boolean isStorageMeasured() {
        return storageMeasured;
    }

    public int getAppsTargeted() {
        return appsTargeted;
    }

    public int getAppsCleared() {
        return appsCleared;
    }

    public int getAppsAlreadyClean() {
        return appsAlreadyClean;
    }

    public int getAppsFailed() {
        return appsFailed;
    }

    public int getAppsCancelled() {
        return appsCancelled;
    }

    public String getSummaryMessage() {
        return summaryMessage;
    }

    public String getDetailNote() {
        return detailNote;
    }

    @NonNull
    public String getFormattedStorageDelta() {
        if (!storageMeasured) {
            return "Unavailable";
        }
        if (bytesDelta > 0) {
            return "+" + StorageMeasurementUtil.formatBytes(bytesDelta);
        } else if (bytesDelta == 0) {
            return "0 B";
        } else {
            return "-" + StorageMeasurementUtil.formatBytes(Math.abs(bytesDelta));
        }
    }

    @NonNull
    public String getFormattedStorageChangeSummary() {
        if (!storageMeasured) {
            return "Storage change: Measurement unavailable";
        }
        if (bytesDelta > 0) {
            return String.format(Locale.US, "Measured storage change: +%s (Free: %s → %s)",
                    StorageMeasurementUtil.formatBytes(bytesDelta),
                    StorageMeasurementUtil.formatBytes(freeBytesBefore),
                    StorageMeasurementUtil.formatBytes(freeBytesAfter));
        } else if (bytesDelta == 0) {
            return String.format(Locale.US, "Measured storage change: 0 B (No change observed; Free: %s)",
                    StorageMeasurementUtil.formatBytes(freeBytesBefore));
        } else {
            return String.format(Locale.US, "Measured storage change: -%s (Storage decreased due to background activity; Free: %s → %s)",
                    StorageMeasurementUtil.formatBytes(Math.abs(bytesDelta)),
                    StorageMeasurementUtil.formatBytes(freeBytesBefore),
                    StorageMeasurementUtil.formatBytes(freeBytesAfter));
        }
    }

    @NonNull
    public String getFormattedDate() {
        SimpleDateFormat sdf = new SimpleDateFormat("MMM d, yyyy • HH:mm:ss", Locale.US);
        return sdf.format(new Date(timestamp));
    }

    @NonNull
    public String getFormattedDuration() {
        if (durationMs < 1000) {
            return durationMs + " ms";
        }
        return String.format(Locale.US, "%.1f s", durationMs / 1000.0);
    }

    // =========================================================================
    // Self-Contained JSON Serialization & Parsing (JVM + Android Portable)
    // =========================================================================

    @NonNull
    public String toJsonString() {
        StringBuilder sb = new StringBuilder();
        sb.append("{");
        sb.append("\"id\":\"").append(escapeJson(id)).append("\",");
        sb.append("\"timestamp\":").append(timestamp).append(",");
        sb.append("\"cleanType\":\"").append(cleanType.name()).append("\",");
        sb.append("\"outcome\":\"").append(outcome.name()).append("\",");
        sb.append("\"durationMs\":").append(durationMs).append(",");
        sb.append("\"freeBytesBefore\":").append(freeBytesBefore).append(",");
        sb.append("\"freeBytesAfter\":").append(freeBytesAfter).append(",");
        sb.append("\"bytesDelta\":").append(bytesDelta).append(",");
        sb.append("\"storageMeasured\":").append(storageMeasured).append(",");
        sb.append("\"appsTargeted\":").append(appsTargeted).append(",");
        sb.append("\"appsCleared\":").append(appsCleared).append(",");
        sb.append("\"appsAlreadyClean\":").append(appsAlreadyClean).append(",");
        sb.append("\"appsFailed\":").append(appsFailed).append(",");
        sb.append("\"appsCancelled\":").append(appsCancelled).append(",");
        sb.append("\"summaryMessage\":\"").append(escapeJson(summaryMessage)).append("\",");
        sb.append("\"detailNote\":\"").append(escapeJson(detailNote)).append("\"");
        sb.append("}");
        return sb.toString();
    }

    @NonNull
    public static CleaningHistoryRecord fromJsonString(@NonNull String json) {
        Map<String, String> map = parseJsonObject(json);

        String id = map.containsKey("id") ? map.get("id") : UUID.randomUUID().toString();
        long timestamp = parseLongOrDefault(map.get("timestamp"), System.currentTimeMillis());

        CleanType cleanType;
        try {
            cleanType = CleanType.valueOf(map.get("cleanType"));
        } catch (Exception e) {
            cleanType = CleanType.QUICK_CLEAN;
        }

        Outcome outcome;
        try {
            outcome = Outcome.valueOf(map.get("outcome"));
        } catch (Exception e) {
            outcome = Outcome.SUCCESS;
        }

        long durationMs = parseLongOrDefault(map.get("durationMs"), 0);
        long freeBytesBefore = parseLongOrDefault(map.get("freeBytesBefore"), -1);
        long freeBytesAfter = parseLongOrDefault(map.get("freeBytesAfter"), -1);
        int appsTargeted = parseIntOrDefault(map.get("appsTargeted"), 0);
        int appsCleared = parseIntOrDefault(map.get("appsCleared"), 0);
        int appsAlreadyClean = parseIntOrDefault(map.get("appsAlreadyClean"), 0);
        int appsFailed = parseIntOrDefault(map.get("appsFailed"), 0);
        int appsCancelled = parseIntOrDefault(map.get("appsCancelled"), 0);
        String summaryMessage = map.containsKey("summaryMessage") ? map.get("summaryMessage") : "";
        String detailNote = map.containsKey("detailNote") ? map.get("detailNote") : "";

        return new CleaningHistoryRecord(
                id,
                timestamp,
                cleanType,
                outcome,
                durationMs,
                freeBytesBefore,
                freeBytesAfter,
                appsTargeted,
                appsCleared,
                appsAlreadyClean,
                appsFailed,
                appsCancelled,
                summaryMessage,
                detailNote
        );
    }

    public JSONObject toJson() {
        try {
            return new JSONObject(toJsonString());
        } catch (Throwable t) {
            return new JSONObject();
        }
    }

    public static CleaningHistoryRecord fromJson(@NonNull JSONObject obj) {
        return fromJsonString(obj.toString());
    }

    // =========================================================================
    // Lightweight JSON Parsing Helpers
    // =========================================================================

    public static String escapeJson(String s) {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': sb.append("\\\""); break;
                case '\\': sb.append("\\\\"); break;
                case '\b': sb.append("\\b"); break;
                case '\f': sb.append("\\f"); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default:
                    if (c < ' ') {
                        String hex = Integer.toHexString(c);
                        sb.append("\\u");
                        for (int k = hex.length(); k < 4; k++) sb.append('0');
                        sb.append(hex);
                    } else {
                        sb.append(c);
                    }
                    break;
            }
        }
        return sb.toString();
    }

    public static String unescapeJson(String s) {
        if (s == null || s.indexOf('\\') == -1) return s;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '\\' && i + 1 < s.length()) {
                char next = s.charAt(i + 1);
                switch (next) {
                    case '"': sb.append('"'); i++; break;
                    case '\\': sb.append('\\'); i++; break;
                    case '/': sb.append('/'); i++; break;
                    case 'b': sb.append('\b'); i++; break;
                    case 'f': sb.append('\f'); i++; break;
                    case 'n': sb.append('\n'); i++; break;
                    case 'r': sb.append('\r'); i++; break;
                    case 't': sb.append('\t'); i++; break;
                    case 'u':
                        if (i + 5 < s.length()) {
                            try {
                                int code = Integer.parseInt(s.substring(i + 2, i + 6), 16);
                                sb.append((char) code);
                                i += 5;
                                break;
                            } catch (NumberFormatException ignored) {}
                        }
                        sb.append(c);
                        break;
                    default:
                        sb.append(c);
                        break;
                }
            } else {
                sb.append(c);
            }
        }
        return sb.toString();
    }

    public static Map<String, String> parseJsonObject(String json) {
        Map<String, String> map = new HashMap<>();
        if (json == null) return map;
        json = json.trim();
        if (!json.startsWith("{") || !json.endsWith("}")) return map;

        int len = json.length();
        int i = 1;
        while (i < len - 1) {
            while (i < len - 1 && Character.isWhitespace(json.charAt(i))) i++;
            if (i >= len - 1 || json.charAt(i) == '}') break;

            if (json.charAt(i) != '"') break;
            i++;
            int keyStart = i;
            while (i < len - 1) {
                if (json.charAt(i) == '"') {
                    int slashes = 0;
                    int k = i - 1;
                    while (k >= keyStart && json.charAt(k) == '\\') {
                        slashes++;
                        k--;
                    }
                    if (slashes % 2 == 0) break;
                }
                i++;
            }
            String key = unescapeJson(json.substring(keyStart, i));
            i++; // skip closing quote

            while (i < len - 1 && json.charAt(i) != ':') i++;
            i++; // skip colon

            while (i < len - 1 && Character.isWhitespace(json.charAt(i))) i++;
            if (i >= len - 1) break;

            String value;
            if (json.charAt(i) == '"') {
                i++;
                int valStart = i;
                while (i < len - 1) {
                    if (json.charAt(i) == '"') {
                        int slashes = 0;
                        int k = i - 1;
                        while (k >= valStart && json.charAt(k) == '\\') {
                            slashes++;
                            k--;
                        }
                        if (slashes % 2 == 0) break;
                    }
                    i++;
                }
                value = unescapeJson(json.substring(valStart, i));
                i++; // skip closing quote
            } else {
                int valStart = i;
                while (i < len - 1 && json.charAt(i) != ',' && json.charAt(i) != '}') {
                    i++;
                }
                value = json.substring(valStart, i).trim();
            }

            map.put(key, value);

            while (i < len - 1 && Character.isWhitespace(json.charAt(i))) i++;
            if (i < len - 1 && json.charAt(i) == ',') {
                i++;
            }
        }
        return map;
    }

    private static long parseLongOrDefault(String s, long def) {
        if (s == null) return def;
        try {
            return Long.parseLong(s.trim());
        } catch (Exception e) {
            return def;
        }
    }

    private static int parseIntOrDefault(String s, int def) {
        if (s == null) return def;
        try {
            return Integer.parseInt(s.trim());
        } catch (Exception e) {
            return def;
        }
    }
}
