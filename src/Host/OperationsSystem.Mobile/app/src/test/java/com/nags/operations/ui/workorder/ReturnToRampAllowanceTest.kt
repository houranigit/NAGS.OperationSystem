package com.nags.operations.ui.workorder

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class ReturnToRampAllowanceTest {
    @Test
    fun existing_revoked_line_blocks_return_to_ramp_until_the_work_order_is_corrected() {
        assertNotNull(
            existingWorkOrderAllowanceError(
                existingServiceIds = listOf("allowed", "revoked"),
                allowedPerformedServiceIds = setOf("allowed"),
            ),
        )
    }

    @Test
    fun existing_allowed_lines_do_not_block_return_to_ramp() {
        assertNull(
            existingWorkOrderAllowanceError(
                existingServiceIds = listOf("allowed"),
                allowedPerformedServiceIds = setOf("allowed"),
            ),
        )
    }
}
