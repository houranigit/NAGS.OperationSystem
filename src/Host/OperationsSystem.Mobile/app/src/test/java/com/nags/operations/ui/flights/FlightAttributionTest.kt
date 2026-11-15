package com.nags.operations.ui.flights

import org.junit.Assert.assertEquals
import org.junit.Test

class FlightAttributionTest {
    @Test fun currentAccountUsesYouRegardlessOfItsRecordedDisplayName() {
        assertEquals("You", attributionName("Previous name", "ACCOUNT-A", "account-a", "You"))
    }

    @Test fun anotherAccountKeepsItsNameEvenWhenTheNamesAreIdentical() {
        assertEquals("Same name", attributionName("Same name", "other-account", "current-account", "You"))
    }

    @Test fun missingAccountIdentityDoesNotIdentifyAnUpdaterByName() {
        assertEquals("Operator", attributionName("Operator", null, "current-account", "You"))
        assertEquals("Operator", attributionName("Operator", "current-account", null, "You"))
    }

    @Test fun legacyActualSubmitterCanUseTheCurrentStaffIdentity() {
        assertEquals("You", attributionName("Operator", null, "account", "You", "STAFF-A", "staff-a"))
        assertEquals("Colleague", attributionName("Colleague", null, "account", "You", "staff-b", "staff-a"))
        assertEquals("Operator", attributionName("Operator", null, "account", "You", "", ""))
    }

    @Test fun explicitAccountIdentityTakesPrecedenceOverStaffIdentity() {
        assertEquals("Other account", attributionName("Other account", "account-b", "account-a", "You", "staff-a", "staff-a"))
    }

    @Test fun creatorAndUpdaterAreLabeledIndependently() {
        assertEquals("Colleague", attributionName("Colleague", "account-b", "account-a", "You"))
        assertEquals("You", attributionName("Operator", "account-a", "account-a", "You"))
    }
}
