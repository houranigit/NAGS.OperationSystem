package com.nags.operations.data.api

import com.nags.operations.data.MobileCatalogsDto
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.MobileMeDto
import com.nags.operations.data.MobileStaffMemberDto
import com.nags.operations.data.TokenStore
import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import com.nags.operations.data.realtime.MobileSyncChangeDto
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import kotlinx.serialization.Serializable

/**
 * Wrapper over the `/api/v1/mobile` endpoint group. Each method maps 1:1 to a server route —
 * there is intentionally no client-side query shaping: the server already returns exactly what
 * the mobile cache needs to mirror.
 *
 * Shares the same JWT-aware [HttpClient] with [AuthApi] so a single 401 triggers the bearer
 * plugin's refresh once for the whole process.
 */
class MobileApi(
    tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    private fun url(path: String) = HttpClientFactory.url(path)

    /** Signed-in staff member's profile — drives the home header greeting + station context. */
    suspend fun me(): MobileMeDto = client.get(url("api/v1/mobile/me")).bodyOrThrow()

    /** Shared lookup catalogs (services, tools, materials, general supports, customers, aircraft types). */
    suspend fun catalogs(): MobileCatalogsDto =
        client.get(url("api/v1/mobile/catalogs")).bodyOrThrow()

    /** Active staff members at the caller's home station. */
    suspend fun myStationEmployees(): List<MobileStaffMemberDto> =
        client.get(url("api/v1/mobile/employees/at-my-station")).bodyOrThrow()

    /** Non-Per-Landing flights the caller is rostered on (±12-hour STA window, server-filtered). */
    suspend fun myFlights(): List<MobileFlightDto> =
        client.get(url("api/v1/mobile/flights/my")).bodyOrThrow()

    /** Per-Landing flights at the caller's station (station-wide visibility by nature). */
    suspend fun perLandingFlights(): List<MobileFlightDto> =
        client.get(url("api/v1/mobile/flights/per-landing")).bodyOrThrow()

    /** Ad Hoc operation-type flights at the caller's station. */
    suspend fun adHocFlights(): List<MobileFlightDto> =
        client.get(url("api/v1/mobile/flights/ad-hoc")).bodyOrThrow()

    /**
     * Single-flight fetch used by real-time sync and notification deep links. It intentionally
     * serves accessible flights outside the list window; callers use the returned window metadata
     * to keep those rows out of Room and render notification details as information-only.
     */
    suspend fun flightById(id: String): MobileFlightDto =
        client.get(url("api/v1/mobile/flights/$id")).bodyOrThrow()

    /** The caller's active work order for a flight, or null. */
    suspend fun myWorkOrderForFlight(flightId: String): WorkOrderDetailWireDto? {
        val response = client.get(url("api/v1/mobile/flights/$flightId/work-orders/mine"))
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(response.status.value, response.bodyAsText())
        }
        val text = response.bodyAsText()
        if (text.isBlank() || text == "null") return null
        return HttpClientFactory.json.decodeFromString(WorkOrderDetailWireDto.serializer(), text)
    }

    /**
     * REST catch-up used after each SignalR (re)connect. `since` carries the max per-table
     * cursor; the server answers with one `refresh` envelope per requested table (pragmatic
     * model — no server-side change log).
     */
    suspend fun syncChanges(
        tables: String? = null,
        since: String? = null,
    ): List<MobileSyncChangeDto> =
        client.get(url("api/v1/mobile/sync/changes")) {
            if (!tables.isNullOrBlank()) parameter("tables", tables)
            if (!since.isNullOrBlank()) parameter("since", since)
        }.bodyOrThrow()

    /**
     * Invite (add) one or more station colleagues onto a flight. Online-only on the client;
     * the inviter is derived server-side from the JWT. Returns no JSON body (HTTP 204).
     */
    suspend fun inviteToFlight(flightId: String, inviteeStaffMemberIds: List<String>) {
        val response = client.post(url("api/v1/mobile/flights/$flightId/invite")) {
            contentType(ContentType.Application.Json)
            setBody(MobileInviteRequest(inviteeStaffMemberIds))
        }
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(response.status.value, response.bodyAsText())
        }
    }

    /**
     * Submit a work order for an existing flight. Called from the offline outbox worker once
     * connectivity returns. `clientMutationId` makes retries idempotent server-side.
     */
    suspend fun createWorkOrderForFlight(
        flightId: String,
        body: MobileWorkOrderWriteRequest,
    ): MobileWriteResult =
        client.post(url("api/v1/mobile/flights/$flightId/work-orders")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /**
     * Create a brand-new ad-hoc flight together with its first work order. `clientFlightId`
     * identifies the offline-created flight across retries; the station is forced server-side
     * to the caller's own station.
     */
    suspend fun createWorkOrderFromScratch(
        body: MobileScratchWorkOrderRequest,
    ): MobileWriteResult =
        client.post(url("api/v1/mobile/work-orders/scratch")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /**
     * Replace an editable (Submitted/Returned) work order. Service lines are clear-and-rebuild;
     * tasks reconcile by id — resend task ids to keep their attachments. The request carries the
     * cached base RowVersion so stale offline edits conflict instead of overwriting newer changes.
     */
    suspend fun updateWorkOrder(
        workOrderId: String,
        body: MobileWorkOrderWriteRequest,
    ): MobileWriteResult =
        client.put(url("api/v1/mobile/work-orders/$workOrderId")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /** Cancel a flight by filing a cancellation work order. */
    suspend fun cancelFlight(
        flightId: String,
        body: MobileCancelFlightRequest,
    ): MobileWriteResult =
        client.post(url("api/v1/mobile/flights/$flightId/cancel")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /** Append return-to-ramp service lines / tasks to the caller's editable work order. */
    suspend fun recordReturnToRamp(
        workOrderId: String,
        body: MobileReturnToRampRequest,
    ): MobileWriteResult =
        client.post(url("api/v1/mobile/work-orders/$workOrderId/return-to-ramp")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()
}

// --- Wire request/response shapes (mirror Operations.Api.Mobile.MobileWriteEndpoints) ---

@Serializable
data class MobileInviteRequest(
    val inviteeStaffMemberIds: List<String>,
)

/**
 * Mirrors the server's `WorkOrderRequest` — the same editable payload the portal uses.
 * `type` is `Completion` or `Cancellation` and controls which fields are required.
 */
@Serializable
data class WorkOrderWireRequest(
    val type: String,
    val actualFlightNumber: String? = null,
    val aircraftTypeId: String? = null,
    val aircraftTailNumber: String? = null,
    val actualArrivalUtc: String? = null,
    val actualDepartureUtc: String? = null,
    val canceledAtUtc: String? = null,
    val cancellationReason: String? = null,
    val remarks: String? = null,
    val serviceLines: List<WorkOrderServiceLineInput> = emptyList(),
    val tasks: List<WorkOrderTaskInput> = emptyList(),
    val customerSignature: WorkOrderSignatureInput? = null,
)

@Serializable
data class WorkOrderServiceLineInput(
    val serviceId: String,
    val performedByStaffMemberId: String,
    val fromUtc: String,
    val toUtc: String,
    val description: String? = null,
)

@Serializable
data class WorkOrderTaskInput(
    /** Stable server task id — resend it on update to keep the task's attachments. Null = new task. */
    val id: String? = null,
    val taskType: String,
    val description: String? = null,
    val fromUtc: String,
    val toUtc: String,
    val employeeIds: List<String> = emptyList(),
    val tools: List<WorkOrderTaskResourceInput> = emptyList(),
    val materials: List<WorkOrderTaskResourceInput> = emptyList(),
    val generalSupports: List<WorkOrderTaskResourceInput> = emptyList(),
    val attachments: List<WorkOrderTaskAttachmentInput> = emptyList(),
)

/**
 * One resource row. The server binds by the field name matching the row kind, so the client
 * sets exactly one of [toolId]/[materialId]/[generalSupportId] per list.
 */
@Serializable
data class WorkOrderTaskResourceInput(
    val toolId: String? = null,
    val materialId: String? = null,
    val generalSupportId: String? = null,
    val quantity: Double = 1.0,
)

@Serializable
data class WorkOrderTaskAttachmentInput(
    val kind: String,
    val base64Content: String,
    val fileName: String,
    val contentType: String,
)

@Serializable
data class WorkOrderSignatureInput(
    val base64Content: String,
    val fileName: String,
    val contentType: String,
)

@Serializable
data class MobileWorkOrderWriteRequest(
    val clientMutationId: String,
    val workOrder: WorkOrderWireRequest,
    /** Base64 concurrency token captured with the editable work order; null for creates. */
    val baseRowVersion: String? = null,
)

@Serializable
data class MobileScratchWorkOrderRequest(
    val clientMutationId: String,
    val clientFlightId: String,
    val customerId: String,
    val flightNumber: String,
    val scheduledArrivalUtc: String,
    val scheduledDepartureUtc: String,
    val aircraftTypeId: String? = null,
    val plannedServiceIds: List<String> = emptyList(),
    val workOrder: WorkOrderWireRequest,
)

@Serializable
data class MobileReturnToRampRequest(
    val clientMutationId: String,
    val serviceLines: List<WorkOrderServiceLineInput> = emptyList(),
    val tasks: List<WorkOrderTaskInput> = emptyList(),
)

@Serializable
data class MobileCancelFlightRequest(
    val clientMutationId: String,
    val canceledAtUtc: String,
    val reason: String,
)

/**
 * Response of every mobile write. [idempotent] is `true` when the server matched the client's
 * mutation id to a prior submission and skipped creating anything new.
 */
@Serializable
data class MobileWriteResult(
    val workOrderId: String,
    val flightId: String,
    val idempotent: Boolean = false,
)
