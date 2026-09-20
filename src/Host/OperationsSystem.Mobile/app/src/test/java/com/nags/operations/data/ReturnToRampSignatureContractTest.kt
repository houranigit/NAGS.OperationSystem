package com.nags.operations.data

import com.nags.operations.data.api.MobileFlightReturnToRampRequest
import com.nags.operations.data.api.WorkOrderReturnToRampInput
import com.nags.operations.data.api.WorkOrderServiceLineInput
import com.nags.operations.data.api.WorkOrderSignatureInput
import com.nags.operations.data.api.WorkOrderTaskInput
import com.nags.operations.data.api.WorkOrderWireRequest
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ReturnToRampSignatureContractTest {
    private val json = Json { encodeDefaults = true; explicitNulls = false }
    private val from = "2026-08-08T10:00:00Z"
    private val to = "2026-08-08T10:30:00Z"
    private val signature = WorkOrderSignatureInput("AQID", "customer-signature.png", "image/png")

    @Test
    fun signatures_belong_to_each_rtr_and_new_lines_do_not_send_legacy_flags() {
        val request = WorkOrderWireRequest(
            type = "Completion",
            returnToRamps = listOf(
                WorkOrderReturnToRampInput(
                    id = "rtr-4", fromUtc = from, toUtc = to,
                    customerSignature = signature,
                    serviceLines = listOf(WorkOrderServiceLineInput(serviceId = "service", fromUtc = from, toUtc = to)),
                    tasks = listOf(WorkOrderTaskInput(taskType = "Minor", fromUtc = from, toUtc = to)),
                ),
                WorkOrderReturnToRampInput(id = "rtr-7", fromUtc = from, toUtc = to, removeCustomerSignature = true),
            ),
        )
        val body = json.parseToJsonElement(json.encodeToString(request)).jsonObject
        val occurrences = body.getValue("returnToRamps").jsonArray
        assertFalse(body.containsKey("customerSignature"))
        assertTrue(body.getValue("serviceLines").jsonArray.isEmpty())
        assertTrue(body.getValue("tasks").jsonArray.isEmpty())
        assertEquals("AQID", occurrences[0].jsonObject.getValue("customerSignature").jsonObject.getValue("base64Content").jsonPrimitive.content)
        assertFalse(occurrences[1].jsonObject.containsKey("customerSignature"))
        assertEquals("true", occurrences[1].jsonObject.getValue("removeCustomerSignature").jsonPrimitive.content)
        assertFalse(occurrences[0].jsonObject.getValue("serviceLines").jsonArray[0].jsonObject.containsKey("isReturnToRamp"))
        assertFalse(occurrences[0].jsonObject.getValue("tasks").jsonArray[0].jsonObject.containsKey("isReturnToRamp"))
    }

    @Test
    fun standalone_flight_action_sends_the_same_optional_signature_contract() {
        val request = MobileFlightReturnToRampRequest(
            clientMutationId = "mutation", fromUtc = from, toUtc = to, customerSignature = signature,
        )
        val body = json.parseToJsonElement(json.encodeToString(request)).jsonObject
        assertEquals("AQID", body.getValue("customerSignature").jsonObject.getValue("base64Content").jsonPrimitive.content)
        assertNull(json.decodeFromString<MobileFlightReturnToRampRequest>(
            """{"clientMutationId":"legacy","fromUtc":"$from","toUtc":"$to"}""",
        ).customerSignature)
    }
}
