package com.nags.operations.data

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderEmailPreferenceContractTest {
    @Test
    fun anOlderProfileResponseDefaultsToOptOut() {
        val profile = Json.decodeFromString<AuthenticatedUser>(
            """{"id":"user","email":"employee@example.test","displayName":"Employee","userType":"Employee"}""",
        )
        assertFalse(profile.receiveWorkOrderSubmissionEmails)
    }

    @Test
    fun canonicalOptInIsReadFromTheSharedIdentityProfile() {
        val profile = Json.decodeFromString<AuthenticatedUser>(
            """{"id":"user","email":"employee@example.test","displayName":"Employee","userType":"Employee","receiveWorkOrderSubmissionEmails":true}""",
        )
        assertTrue(profile.receiveWorkOrderSubmissionEmails)
    }

    @Test
    fun preferenceUpdatesContainOnlyTheEnabledFlag() {
        assertEquals("""{"enabled":true}""", Json.encodeToString(WorkOrderEmailPreferenceRequest(true)))
        assertEquals("""{"enabled":false}""", Json.encodeToString(WorkOrderEmailPreferenceRequest(false)))
    }
}
