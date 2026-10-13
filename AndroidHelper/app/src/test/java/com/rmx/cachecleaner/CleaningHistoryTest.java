package com.rmx.cachecleaner;

import org.json.JSONObject;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;

import java.util.List;

import static org.junit.Assert.*;

/**
 * Unit tests for Cleaning Results & History features:
 * 1. Storage measurement delta calculations (positive reclaimed, zero unchanged, negative fluctuation, unavailable).
 * 2. Honest labeling and transparent error/fluctuation explanations.
 * 3. CleaningHistoryRecord model creation, validation, and JSON roundtrip serialization.
 * 4. CleaningHistoryRepository persistence, ordering (newest first), 50-item bounding cap, and clearing.
 * 5. Outcome classification (SUCCESS, PARTIAL, FAILED, CANCELLED).
 */
public class CleaningHistoryTest {

    private static class InMemoryPersistenceDelegate implements CleaningHistoryRepository.PersistenceDelegate {
        private String storedJson = null;

        @Override
        public String loadRawJson() {
            return storedJson;
        }

        @Override
        public void saveRawJson(String rawJson) {
            this.storedJson = rawJson;
        }
    }

    private InMemoryPersistenceDelegate memoryPersistence;
    private CleaningHistoryRepository repository;

    @Before
    public void setUp() {
        CleaningHistoryRepository.resetInstanceForTesting();
        StorageMeasurementUtil.resetStorageProviderForTesting();

        memoryPersistence = new InMemoryPersistenceDelegate();
        repository = CleaningHistoryRepository.getInstance(null);
        repository.setPersistenceDelegateForTesting(memoryPersistence);
    }

    @After
    public void tearDown() {
        CleaningHistoryRepository.resetInstanceForTesting();
        StorageMeasurementUtil.resetStorageProviderForTesting();
    }

    // =========================================================================
    // 1. Storage Measurement Tests
    // =========================================================================

    @Test
    public void testStorageMeasurement_positiveReclaimed() {
        long before = 10_000_000_000L; // 10 GB
        long after = 10_500_000_000L;  // 10.5 GB (+500 MB)

        StorageMeasurementUtil.StorageMeasurement m =
                StorageMeasurementUtil.calculateMeasurement(before, after);

        assertTrue(m.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.POSITIVE_RECLAIMED, m.deltaType);
        assertEquals(500_000_000L, m.deltaBytes);
        assertTrue(m.getFormattedDelta().startsWith("+"));
        assertTrue(m.getDescription().contains("Measured storage change: +"));
        assertTrue(m.getDescription().contains("Free before:"));
        assertTrue(m.getDescription().contains("Free after:"));
    }

    @Test
    public void testStorageMeasurement_zeroUnchanged() {
        long before = 15_000_000_000L;
        long after = 15_000_000_000L;

        StorageMeasurementUtil.StorageMeasurement m =
                StorageMeasurementUtil.calculateMeasurement(before, after);

        assertTrue(m.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.ZERO_UNCHANGED, m.deltaType);
        assertEquals(0L, m.deltaBytes);
        assertEquals("0 B", m.getFormattedDelta());
        assertTrue(m.getDescription().contains("No storage change observed"));
    }

    @Test
    public void testStorageMeasurement_negativeFluctuation() {
        long before = 10_000_000_000L;
        long after = 9_900_000_000L; // -100 MB

        StorageMeasurementUtil.StorageMeasurement m =
                StorageMeasurementUtil.calculateMeasurement(before, after);

        assertTrue(m.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.NEGATIVE_FLUCTUATION, m.deltaType);
        assertEquals(-100_000_000L, m.deltaBytes);
        assertTrue(m.getFormattedDelta().startsWith("-"));
        assertTrue(m.getDescription().contains("Storage decreased by"));
        assertTrue(m.getDescription().contains("background system activity"));
    }

    @Test
    public void testStorageMeasurement_unavailableMeasurement() {
        // Test with invalid / unavailable before value (-1)
        StorageMeasurementUtil.StorageMeasurement m1 =
                StorageMeasurementUtil.calculateMeasurement(-1, 10_000_000_000L);
        assertFalse(m1.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.UNAVAILABLE, m1.deltaType);
        assertEquals("Unavailable", m1.getFormattedDelta());
        assertTrue(m1.getDescription().contains("unavailable"));

        // Test with zero before value
        StorageMeasurementUtil.StorageMeasurement m2 =
                StorageMeasurementUtil.calculateMeasurement(0, 10_000_000_000L);
        assertFalse(m2.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.UNAVAILABLE, m2.deltaType);

        // Test with negative after value
        StorageMeasurementUtil.StorageMeasurement m3 =
                StorageMeasurementUtil.calculateMeasurement(10_000_000_000L, -1);
        assertFalse(m3.isValid);
        assertEquals(StorageMeasurementUtil.DeltaType.UNAVAILABLE, m3.deltaType);
    }

    @Test
    public void testStorageMeasurement_customProviderInjection() {
        StorageMeasurementUtil.setStorageProviderForTesting(new StorageMeasurementUtil.StorageProvider() {
            @Override
            public long getAvailableBytes() {
                return 42_000_000_000L;
            }
        });

        assertEquals(42_000_000_000L, StorageMeasurementUtil.getAvailableStorageBytes());
    }

    // =========================================================================
    // 2. CleaningHistoryRecord Model & Outcome Tests
    // =========================================================================

    @Test
    public void testRecordCreation_successfulQuickClean() {
        CleaningHistoryRecord record = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS,
                1450L,
                10_000_000_000L,
                10_250_000_000L,
                "Bulk cache trimmed successfully!"
        );

        assertEquals(CleaningHistoryRecord.CleanType.QUICK_CLEAN, record.getCleanType());
        assertEquals(CleaningHistoryRecord.Outcome.SUCCESS, record.getOutcome());
        assertEquals(1450L, record.getDurationMs());
        assertEquals(250_000_000L, record.getBytesDelta());
        assertTrue(record.isStorageMeasured());
        assertTrue(record.getFormattedStorageDelta().startsWith("+"));
        assertTrue(record.getSummaryMessage().contains("Bulk cache trimmed"));
        assertNotNull(record.getId());
        assertTrue(record.getTimestamp() > 0);
    }

    @Test
    public void testRecordCreation_failedQuickClean() {
        CleaningHistoryRecord record = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.FAILED,
                200L,
                -1,
                -1,
                "Shizuku service not running"
        );

        assertEquals(CleaningHistoryRecord.Outcome.FAILED, record.getOutcome());
        assertFalse(record.isStorageMeasured());
        assertEquals("Unavailable", record.getFormattedStorageDelta());
    }

    @Test
    public void testRecordCreation_successfulDeepClean() {
        CleaningHistoryRecord record = CleaningHistoryRecord.createDeepClean(
                CleaningHistoryRecord.Outcome.SUCCESS,
                8500L,
                10_000_000_000L,
                10_080_000_000L,
                5,
                4,
                1,
                0,
                0,
                "Batch completed successfully!"
        );

        assertEquals(CleaningHistoryRecord.CleanType.DEEP_CLEAN, record.getCleanType());
        assertEquals(CleaningHistoryRecord.Outcome.SUCCESS, record.getOutcome());
        assertEquals(5, record.getAppsTargeted());
        assertEquals(4, record.getAppsCleared());
        assertEquals(1, record.getAppsAlreadyClean());
        assertEquals(0, record.getAppsFailed());
        assertEquals(0, record.getAppsCancelled());
        assertEquals(80_000_000L, record.getBytesDelta());
        assertTrue(record.getFormattedDuration().contains("8.5 s"));
    }

    @Test
    public void testRecordCreation_partialDeepClean() {
        CleaningHistoryRecord record = CleaningHistoryRecord.createDeepClean(
                CleaningHistoryRecord.Outcome.PARTIAL,
                12000L,
                10_000_000_000L,
                10_020_000_000L,
                6,
                3,
                1,
                2,
                0,
                "Batch finished with partial issues"
        );

        assertEquals(CleaningHistoryRecord.Outcome.PARTIAL, record.getOutcome());
        assertEquals(2, record.getAppsFailed());
        assertEquals(3, record.getAppsCleared());
    }

    @Test
    public void testRecordCreation_cancelledDeepClean() {
        CleaningHistoryRecord record = CleaningHistoryRecord.createDeepClean(
                CleaningHistoryRecord.Outcome.CANCELLED,
                3200L,
                10_000_000_000L,
                10_010_000_000L,
                10,
                2,
                0,
                0,
                8,
                "Batch cancelled by user."
        );

        assertEquals(CleaningHistoryRecord.Outcome.CANCELLED, record.getOutcome());
        assertEquals(8, record.getAppsCancelled());
        assertEquals(2, record.getAppsCleared());
    }

    @Test
    public void testRecordJsonSerializationRoundtrip() throws Exception {
        CleaningHistoryRecord original = new CleaningHistoryRecord(
                "test-uuid-1234",
                1700000000000L,
                CleaningHistoryRecord.CleanType.DEEP_CLEAN,
                CleaningHistoryRecord.Outcome.SUCCESS,
                5432L,
                12_345_678_000L,
                12_890_123_000L,
                4,
                3,
                1,
                0,
                0,
                "Batch verified live",
                "Settings automation"
        );

        String json = original.toJsonString();
        assertNotNull(json);
        assertTrue(json.startsWith("{") && json.endsWith("}"));

        CleaningHistoryRecord restored = CleaningHistoryRecord.fromJsonString(json);

        assertEquals(original.getId(), restored.getId());
        assertEquals(original.getTimestamp(), restored.getTimestamp());
        assertEquals(original.getCleanType(), restored.getCleanType());
        assertEquals(original.getOutcome(), restored.getOutcome());
        assertEquals(original.getDurationMs(), restored.getDurationMs());
        assertEquals(original.getFreeBytesBefore(), restored.getFreeBytesBefore());
        assertEquals(original.getFreeBytesAfter(), restored.getFreeBytesAfter());
        assertEquals(original.getBytesDelta(), restored.getBytesDelta());
        assertEquals(original.isStorageMeasured(), restored.isStorageMeasured());
        assertEquals(original.getAppsTargeted(), restored.getAppsTargeted());
        assertEquals(original.getAppsCleared(), restored.getAppsCleared());
        assertEquals(original.getAppsAlreadyClean(), restored.getAppsAlreadyClean());
        assertEquals(original.getAppsFailed(), restored.getAppsFailed());
        assertEquals(original.getAppsCancelled(), restored.getAppsCancelled());
        assertEquals(original.getSummaryMessage(), restored.getSummaryMessage());
        assertEquals(original.getDetailNote(), restored.getDetailNote());
    }

    // =========================================================================
    // 3. Repository Persistence & Bounding Tests
    // =========================================================================

    @Test
    public void testRepository_initiallyEmpty() {
        assertEquals(0, repository.getRecordCount());
        assertNull(repository.getLatestRecord());
        assertTrue(repository.getRecords().isEmpty());
    }

    @Test
    public void testRepository_addAndGetLatest() {
        CleaningHistoryRecord r1 = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 1000L, 2000L, "First run");

        repository.addRecord(r1);

        assertEquals(1, repository.getRecordCount());
        assertEquals(r1.getId(), repository.getLatestRecord().getId());
    }

    @Test
    public void testRepository_ordersNewestFirst() {
        CleaningHistoryRecord r1 = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 1000L, 2000L, "Run 1");
        CleaningHistoryRecord r2 = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1200L, 2000L, 3000L, "Run 2");

        repository.addRecord(r1);
        repository.addRecord(r2);

        List<CleaningHistoryRecord> all = repository.getRecords();
        assertEquals(2, all.size());
        assertEquals(r2.getId(), all.get(0).getId()); // Newest first
        assertEquals(r1.getId(), all.get(1).getId());
        assertEquals(r2.getId(), repository.getLatestRecord().getId());
    }

    @Test
    public void testRepository_boundsTo50Entries() {
        // Insert 60 entries
        for (int i = 1; i <= 60; i++) {
            CleaningHistoryRecord r = CleaningHistoryRecord.createQuickClean(
                    CleaningHistoryRecord.Outcome.SUCCESS,
                    100L * i,
                    1_000_000L * i,
                    1_000_000L * i + 10_000L,
                    "Run #" + i
            );
            repository.addRecord(r);
        }

        // Must be capped at exactly MAX_ENTRIES = 50
        assertEquals(50, repository.getRecordCount());

        List<CleaningHistoryRecord> all = repository.getRecords();
        assertEquals(50, all.size());

        // Newest must be Run #60
        assertEquals("Run #60", all.get(0).getSummaryMessage());
        // Oldest retained must be Run #11 (runs 1..10 dropped)
        assertEquals("Run #11", all.get(49).getSummaryMessage());
    }

    @Test
    public void testRepository_clearHistory() {
        repository.addRecord(CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 1000L, 2000L, "Run 1"));
        repository.addRecord(CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 1000L, 2000L, "Run 2"));

        assertEquals(2, repository.getRecordCount());

        repository.clearHistory();

        assertEquals(0, repository.getRecordCount());
        assertNull(repository.getLatestRecord());
        assertTrue(repository.getRecords().isEmpty());
        assertNull(memoryPersistence.loadRawJson());
    }

    @Test
    public void testRepository_persistsAcrossReload() {
        CleaningHistoryRecord r1 = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 5_000_000L, 6_000_000L, "Persisted Run 1");
        CleaningHistoryRecord r2 = CleaningHistoryRecord.createDeepClean(
                CleaningHistoryRecord.Outcome.PARTIAL, 4000L, 6_000_000L, 7_000_000L, 3, 2, 0, 1, 0, "Persisted Run 2");

        repository.addRecord(r1);
        repository.addRecord(r2);

        // Verify JSON was written to persistence delegate
        assertNotNull(memoryPersistence.loadRawJson());

        // Simulate new repository instance loading from the same persistence store
        CleaningHistoryRepository.resetInstanceForTesting();
        CleaningHistoryRepository newRepo = CleaningHistoryRepository.getInstance(null);
        newRepo.setPersistenceDelegateForTesting(memoryPersistence);

        assertEquals(2, newRepo.getRecordCount());
        assertEquals(r2.getId(), newRepo.getLatestRecord().getId());
        assertEquals("Persisted Run 2", newRepo.getRecords().get(0).getSummaryMessage());
        assertEquals("Persisted Run 1", newRepo.getRecords().get(1).getSummaryMessage());
    }

    // =========================================================================
    // 4. Honesty & Safety Invariants
    // =========================================================================

    @Test
    public void testHonesty_neverClaimsExactCacheCleared() {
        StorageMeasurementUtil.StorageMeasurement m =
                StorageMeasurementUtil.calculateMeasurement(10_000_000_000L, 10_050_000_000L);

        // Must label as measured storage change, not exact cache cleared
        String desc = m.getDescription();
        assertTrue(desc.contains("Measured storage change"));
        assertFalse(desc.toLowerCase().contains("guaranteed cache deleted"));

        CleaningHistoryRecord record = CleaningHistoryRecord.createQuickClean(
                CleaningHistoryRecord.Outcome.SUCCESS, 1000L, 10_000_000_000L, 10_050_000_000L, "Quick Clean Test");
        String summary = record.getFormattedStorageChangeSummary();
        assertTrue(summary.contains("Measured storage change"));
    }

    // =========================================================================
    // 5. Batch Outcome & Safety Invariant Tests
    // =========================================================================

    @Test
    public void testEvaluateBatchOutcome_allSuccessful() {
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch completed successfully.", 3, 2, 0, 0);
        assertEquals(CleaningHistoryRecord.Outcome.SUCCESS, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_allAlreadyClean() {
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch completed: All apps were already clean.", 0, 4, 0, 0);
        assertEquals(CleaningHistoryRecord.Outcome.SUCCESS, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_cancelledByUser() {
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch cancelled by user.\nResults before cancel: 1 cleared, 0 already clean, 0 failed, 3 stopped/cancelled.",
                1, 0, 0, 3);
        assertEquals(CleaningHistoryRecord.Outcome.CANCELLED, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_allFailed() {
        // Zero apps cleared or clean; all failed -> FAILED, not PARTIAL
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch finished with errors.", 0, 0, 3, 0);
        assertEquals(CleaningHistoryRecord.Outcome.FAILED, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_partialWithSomeSuccessAndFailures() {
        // Some succeeded, some failed -> PARTIAL
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch completed with 1 error.", 2, 1, 1, 0);
        assertEquals(CleaningHistoryRecord.Outcome.PARTIAL, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_fatalErrorWithZeroSuccess() {
        // Fatal disconnect before any apps could be cleaned -> FAILED
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch stopped due to fatal error: Accessibility Service disconnected during pilot execution.\nResults: 0 cleared, 0 already clean, 1 failed, 2 skipped.",
                0, 0, 1, 2);
        assertEquals(CleaningHistoryRecord.Outcome.FAILED, outcome);
    }

    @Test
    public void testEvaluateBatchOutcome_fatalErrorWithPartialSuccess() {
        // Fatal disconnect after 1 app was cleaned -> PARTIAL
        CleaningHistoryRecord.Outcome outcome = CleaningHistoryRecord.evaluateBatchOutcome(
                "Batch stopped due to fatal error: Accessibility Service disconnected during pilot execution.\nResults: 1 cleared, 0 already clean, 1 failed, 1 skipped.",
                1, 0, 1, 1);
        assertEquals(CleaningHistoryRecord.Outcome.PARTIAL, outcome);
    }
}
