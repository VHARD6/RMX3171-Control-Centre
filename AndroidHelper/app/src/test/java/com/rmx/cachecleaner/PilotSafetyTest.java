package com.rmx.cachecleaner;

import org.junit.Test;
import static org.junit.Assert.*;

/**
 * Phase 4.4: Unit Tests for Single-App Pilot Safety Hardening
 *
 * Verifies all 8 critical edge cases without interacting with Android Settings or clearing cache:
 * 1. Label matching & whitespace normalization (\u00A0, \u202F)
 * 2. Strict size string validation
 * 3. Rejection of forbidden sections ("Total: 580 MB", "App size", "Data")
 * 4. Zero-cache string identification
 * 5. Decision Matrix Case 1: Exactly 1 enabled clickable button + positive cache -> PROCEED_TO_CONFIRMATION
 * 6. Decision Matrix Case 2: Exactly 1 disabled button + verified zero cache -> CONCLUDE_ALREADY_CLEAN
 * 7. Decision Matrix Case 3: Disabled button + positive cache reading -> ABORT_DISABLED_WITH_POSITIVE_CACHE
 * 8. Decision Matrix Case 4: Missing or duplicate buttons (0 or >1 buttons) -> ABORT_INVALID_BUTTON_COUNT
 * 9. Decision Matrix Case 5: Untrustworthy or unparsed cache string -> ABORT_UNTRUSTWORTHY_CACHE_SIZE
 * 10. Decision Matrix Case 6: Enabled but non-clickable button -> ABORT_NON_CLICKABLE
 * 11. Monotonic session token validation and stale callback rejection
 */
public class PilotSafetyTest {

    // =========================================================================
    // Edge Case 1 & 2: Normalization and Whitespace Handling
    // =========================================================================

    @Test
    public void testNormalizeSizeString_removesNonBreakingSpaces() {
        // ColorOS 11 uses non-breaking space (\u00A0) and narrow non-breaking space (\u202F) between number and unit
        String withNBSP = "59.7\u00A0MB";
        assertEquals("59.7 MB", PilotController.normalizeSizeString(withNBSP));

        String withNarrowNBSP = "0\u202FB";
        assertEquals("0 B", PilotController.normalizeSizeString(withNarrowNBSP));

        String withStandardPadding = "   120 KB   ";
        assertEquals("120 KB", PilotController.normalizeSizeString(withStandardPadding));

        assertEquals("", PilotController.normalizeSizeString(null));
        assertEquals("", PilotController.normalizeSizeString(""));
    }

    // =========================================================================
    // Edge Case 3 & 4: Strict Size String Parsing & Rejection of Forbidden Sections
    // =========================================================================

    @Test
    public void testIsStrictSizeString_acceptsValidMetrics() {
        assertTrue(PilotController.isStrictSizeString("0 B"));
        assertTrue(PilotController.isStrictSizeString("0\u00A0B"));
        assertTrue(PilotController.isStrictSizeString("59.7 MB"));
        assertTrue(PilotController.isStrictSizeString("59.7\u00A0MB"));
        assertTrue(PilotController.isStrictSizeString("1.25 GB"));
        assertTrue(PilotController.isStrictSizeString("512 KB"));
        assertTrue(PilotController.isStrictSizeString("10 TB"));
        assertTrue(PilotController.isStrictSizeString("0B"));
    }

    @Test
    public void testIsStrictSizeString_rejectsForbiddenSectionsAndMalformedValues() {
        // Forbidden screen-level labels
        assertFalse("Must reject Total section string", PilotController.isStrictSizeString("Total: 580 MB"));
        assertFalse("Must reject App size string", PilotController.isStrictSizeString("App size: 210 MB"));
        assertFalse("Must reject Data section string", PilotController.isStrictSizeString("Data: 360 MB"));
        assertFalse("Must reject standalone label", PilotController.isStrictSizeString("Total"));
        assertFalse("Must reject standalone label", PilotController.isStrictSizeString("App size"));
        assertFalse("Must reject standalone label", PilotController.isStrictSizeString("Data"));
        assertFalse("Must reject standalone label", PilotController.isStrictSizeString("Cache"));
        assertFalse("Must reject standalone label", PilotController.isStrictSizeString("Storage usage"));

        // Malformed or pending states
        assertFalse("Must reject pending state", PilotController.isStrictSizeString("Calculating..."));
        assertFalse("Must reject unknown state", PilotController.isStrictSizeString("Unknown"));
        assertFalse("Must reject empty string", PilotController.isStrictSizeString(""));
        assertFalse("Must reject null", PilotController.isStrictSizeString(null));
        assertFalse("Must reject number without unit", PilotController.isStrictSizeString("59.7"));
        assertFalse("Must reject unit without number", PilotController.isStrictSizeString("MB"));
    }

    // =========================================================================
    // Edge Case 5: Zero-Cache Recognition
    // =========================================================================

    @Test
    public void testIsZeroCacheString_identifiesZeroReadings() {
        assertTrue(PilotController.isZeroCacheString("0 B"));
        assertTrue(PilotController.isZeroCacheString("0\u00A0B"));
        assertTrue(PilotController.isZeroCacheString("0\u202FB"));
        assertTrue(PilotController.isZeroCacheString("0.0 B"));
        assertTrue(PilotController.isZeroCacheString("0.00 B"));
        assertTrue(PilotController.isZeroCacheString("0.0 MB"));
        assertTrue(PilotController.isZeroCacheString("0.00 MB"));
        assertTrue(PilotController.isZeroCacheString("0 MB"));
        assertTrue(PilotController.isZeroCacheString("0B"));
        assertTrue(PilotController.isZeroCacheString("0"));
    }

    @Test
    public void testIsZeroCacheString_rejectsNonZeroAndInvalidReadings() {
        assertFalse("1 B is not zero", PilotController.isZeroCacheString("1 B"));
        assertFalse("59.7 MB is not zero", PilotController.isZeroCacheString("59.7 MB"));
        assertFalse("0.1 MB is not zero", PilotController.isZeroCacheString("0.1 MB"));
        assertFalse("128 KB is not zero", PilotController.isZeroCacheString("128 KB"));
        assertFalse("Total string must not be zero cache", PilotController.isZeroCacheString("Total: 0 B"));
        assertFalse("Empty string is not zero", PilotController.isZeroCacheString(""));
        assertFalse("Null is not zero", PilotController.isZeroCacheString(null));
        assertFalse("Calculating is not zero", PilotController.isZeroCacheString("Calculating..."));
    }

    // =========================================================================
    // Edge Case 6 & 7 & 8: Decision Matrix (4-Way Zero-Cache & Safety Handling)
    // =========================================================================

    @Test
    public void testDecisionMatrix_Case1_ProceedToConfirmation() {
        // Exactly 1 enabled, clickable button with trustworthy positive cache reading
        PilotController.CacheButtonDecision decision = PilotController.evaluateCacheButtonState(
                1,      // buttonCount
                true,   // isEnabled
                true,   // isClickable
                "59.7 MB" // observedCache
        );
        assertEquals(PilotController.CacheButtonDecision.PROCEED_TO_CONFIRMATION, decision);
    }

    @Test
    public void testDecisionMatrix_Case2_AlreadyClean_SafeConcludeWithoutClick() {
        // Exactly 1 disabled button with trustworthy zero cache reading (Realme RMX3171 ColorOS 11 state)
        PilotController.CacheButtonDecision decision = PilotController.evaluateCacheButtonState(
                1,      // buttonCount
                false,  // isEnabled = false
                false,  // isClickable = false
                "0\u00A0B" // observedCache with ColorOS NBSP
        );
        assertEquals(PilotController.CacheButtonDecision.CONCLUDE_ALREADY_CLEAN, decision);

        // Also test with standard "0 B"
        PilotController.CacheButtonDecision decision2 = PilotController.evaluateCacheButtonState(
                1, false, false, "0 B"
        );
        assertEquals(PilotController.CacheButtonDecision.CONCLUDE_ALREADY_CLEAN, decision2);
    }

    @Test
    public void testDecisionMatrix_Case3_AbortDisabledWithPositiveCache() {
        // Disabled button with positive cache reading -> must abort immediately
        PilotController.CacheButtonDecision decision = PilotController.evaluateCacheButtonState(
                1,
                false, // isEnabled = false
                false,
                "59.7 MB" // observedCache is positive!
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_DISABLED_WITH_POSITIVE_CACHE, decision);
    }

    @Test
    public void testDecisionMatrix_Case4_AbortInvalidButtonCount() {
        // Missing button (0 matches)
        PilotController.CacheButtonDecision decision0 = PilotController.evaluateCacheButtonState(
                0, true, true, "59.7 MB"
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_INVALID_BUTTON_COUNT, decision0);

        // Duplicate buttons (>1 matches, e.g. ambiguous layout)
        PilotController.CacheButtonDecision decision2 = PilotController.evaluateCacheButtonState(
                2, true, true, "59.7 MB"
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_INVALID_BUTTON_COUNT, decision2);
    }

    @Test
    public void testDecisionMatrix_Case5_AbortUntrustworthyCacheSize() {
        // Null cache
        PilotController.CacheButtonDecision decisionNull = PilotController.evaluateCacheButtonState(
                1, true, true, null
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_UNTRUSTWORTHY_CACHE_SIZE, decisionNull);

        // Unparsed string
        PilotController.CacheButtonDecision decisionCalc = PilotController.evaluateCacheButtonState(
                1, true, true, "Calculating..."
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_UNTRUSTWORTHY_CACHE_SIZE, decisionCalc);

        // Total label mistakenly extracted
        PilotController.CacheButtonDecision decisionTotal = PilotController.evaluateCacheButtonState(
                1, true, true, "Total: 580 MB"
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_UNTRUSTWORTHY_CACHE_SIZE, decisionTotal);
    }

    @Test
    public void testDecisionMatrix_Case6_AbortNonClickable() {
        // Button is enabled according to accessibility node, but isClickable is false
        PilotController.CacheButtonDecision decision = PilotController.evaluateCacheButtonState(
                1,
                true,  // isEnabled
                false, // isClickable = false
                "59.7 MB"
        );
        assertEquals(PilotController.CacheButtonDecision.ABORT_NON_CLICKABLE, decision);
    }

    // =========================================================================
    // Edge Case 9: Session Token Callback Invalidation
    // =========================================================================

    @Test
    public void testSessionTokenValidation_rejectsStaleCallbacks() {
        PilotController controller = PilotController.getInstance();
        int currentToken = controller.getActiveSessionToken();

        // Stale token from earlier session should be rejected
        assertFalse(controller.isCallbackValid(currentToken - 1, PilotController.State.IDLE));
        assertFalse(controller.isCallbackValid(currentToken + 1, PilotController.State.IDLE));
    }
}
