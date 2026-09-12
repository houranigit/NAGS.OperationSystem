package com.nags.operations.ui.flights

import com.nags.operations.data.WellKnownMasterDataIds
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class CancellationCustomerNotesTest {
    @Test
    fun unknown_customer_legacy_cancellation_uses_the_required_reason_when_notes_are_missing() {
        assertEquals("Customer canceled", cancellationRemarksForCustomer(WellKnownMasterDataIds.UnknownCustomer, null, "Customer canceled"))
        assertEquals("Customer detail", cancellationRemarksForCustomer(WellKnownMasterDataIds.UnknownCustomer, "Customer detail", "Customer canceled"))
        assertNull(cancellationRemarksForCustomer("known", null, "Customer canceled"))
    }
}
