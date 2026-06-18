package com.nags.operations.data.api

import com.nags.operations.data.EmployeeSnapshotDto
import com.nags.operations.data.MobileAogFlightSummaryDto
import com.nags.operations.data.MobileFlightSummaryDto
import com.nags.operations.data.MobileMeDto
import com.nags.operations.data.MobileV2CatalogsDto
import com.nags.operations.data.TokenStore
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
 * Wrapper over the `/api/mobile/v2` endpoint group. Each method maps 1:1 to a
 * server route — there is intentionally no client-side query shaping (no
 * pagination, no filters): the server already returns exactly what the
 * mobile cache needs to mirror.
 *
 * Shares the same JWT-aware [HttpClient] with [AuthApi] so a single 401
 * triggers the bearer plugin's refresh once for the whole process.
 */
class MobileApi(
    tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    private fun url(path: String) = HttpClientFactory.url(path)

    /** Logged-in employee's profile — drives the home header greeting. */
    suspend fun me(): MobileMeDto = client.get(url("api/mobile/v2/me")).bodyOrThrow()

    /** Shared lookup catalogs (services, tools, materials, general supports, customers). */
    suspend fun catalogs(): MobileV2CatalogsDto =
        client.get(url("api/mobile/v2/catalogs")).bodyOrThrow()

    /** Active employees at the caller's home station. */
    suspend fun myStationEmployees(): List<EmployeeSnapshotDto> =
        client.get(url("api/mobile/v2/employees/at-my-station")).bodyOrThrow()

    /** Non-AOG flights the caller is rostered on (±12-hour STA window, server-filtered). */
    suspend fun myFlights(): List<MobileFlightSummaryDto> =
        client.get(url("api/mobile/v2/flights/my")).bodyOrThrow()

    /**
     * AOG flights at the caller's station (±12-hour STA window, server-filtered). Comes back
     * without the per-flight `services` list — the AOG chip is implicit on this
     * tab and the server elides the JOIN to keep the payload lean.
     */
    suspend fun aogFlights(): List<MobileAogFlightSummaryDto> =
        client.get(url("api/mobile/v2/flights/aog")).bodyOrThrow()

    /**
     * Ad Hoc operation-type flights at the caller's station (±12-hour STA window,
     * server-filtered). Same lean shape as [aogFlights].
     */
    suspend fun adHocFlights(): List<MobileAogFlightSummaryDto> =
        client.get(url("api/mobile/v2/flights/ad-hoc")).bodyOrThrow()

    /**
     * Single-flight fetch used by the real-time sync apply path. When the server
     * pushes an `upsert` for a flight the realtime channel calls this to project
     * just that row in the same shape as [myFlights] / [aogFlights], then upserts
     * into the matching local Room table.
     */
    suspend fun flightById(id: String): MobileFlightSummaryDto =
        client.get(url("api/mobile/v2/flights/$id")).bodyOrThrow()

    /**
     * REST catch-up endpoint used after each SignalR (re)connect. We pass the
     * caller's per-table cursors as a single `since=` (max across all tables) so
     * the server can short-circuit and return one `refresh` envelope per table —
     * cheaper than re-fetching every table blindly. `tables` is a CSV; if null,
     * the server returns refresh envelopes for every mobile cache table.
     */
    suspend fun syncChanges(
        tables: String? = null,
        since: String? = null,
    ): List<MobileSyncChangeDto> =
        client.get(url("api/mobile/v2/sync/changes")) {
            if (!tables.isNullOrBlank()) parameter("tables", tables)
            if (!since.isNullOrBlank()) parameter("since", since)
        }.bodyOrThrow()

    /**
     * Invite (assign) one or more station colleagues to a flight in a single request.
     * Online-only on the client; the inviter is derived server-side from the JWT, so only
     * the invitee ids travel in the body. Returns no JSON body (HTTP 204).
     */
    suspend fun inviteToFlight(flightId: String, inviteeEmployeeIds: List<String>) {
        val response = client.post(url("api/mobile/v2/flights/$flightId/invite")) {
            contentType(ContentType.Application.Json)
            setBody(MobileV2InviteRequest(inviteeEmployeeIds))
        }
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(
                response.status.value,
                response.bodyAsText(),
            )
        }
    }

    /**
     * POST a work order for an existing flight. Called from the offline outbox
     * worker once connectivity returns; the mobile UI never invokes this path
     * synchronously from the create screen. The required `clientMutationId`
     * lets the server dedupe retries — see [CreateWorkOrderResponse.idempotent].
     */
    suspend fun createWorkOrderForFlight(
        flightId: String,
        body: MobileCreateWorkOrderRequest,
    ): CreateWorkOrderResponse =
        client.post(url("api/mobile/v2/flights/$flightId/work-orders")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /**
     * POST a brand-new ad-hoc flight together with its work order. The
     * `clientFlightId` and `clientMutationId` together let the server reject
     * duplicate retries of the same intent — the response carries the server's
     * assigned ids.
     */
    suspend fun createWorkOrderFromScratch(
        body: MobileCreateFromScratchRequest,
    ): CreateWorkOrderResponse =
        client.post(url("api/mobile/v2/work-orders/scratch")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /**
     * PUT an existing under-review work order. Returns no JSON body (HTTP 204).
     * [MobileUpdateWorkOrderRequest.clientMutationId] correlates with SignalR `originMutationId`.
     */
    suspend fun updateWorkOrder(workOrderId: String, body: MobileUpdateWorkOrderRequest) {
        val response = client.put(url("api/mobile/v2/work-orders/$workOrderId")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(
                response.status.value,
                response.bodyAsText(),
            )
        }
    }

    /**
     * POST a flight cancellation. Called from the offline outbox worker once
     * connectivity returns; files an empty cancel work order server-side. The
     * required `clientMutationId` lets the server dedupe retries — see
     * [CreateWorkOrderResponse.idempotent].
     */
    suspend fun cancelFlight(
        flightId: String,
        body: MobileCancelFlightRequest,
    ): CreateWorkOrderResponse =
        client.post(url("api/mobile/v2/flights/$flightId/cancel")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }.bodyOrThrow()

    /** POST append-only return-to-ramp lines (HTTP 204). */
    suspend fun recordReturnToRamp(workOrderId: String, body: MobileReturnToRampRequest) {
        val response = client.post(url("api/mobile/v2/work-orders/$workOrderId/return-to-ramp")) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(
                response.status.value,
                response.bodyAsText(),
            )
        }
    }
}

/**
 * Wire shape for `POST /api/mobile/v2/flights/{id}/invite` — mirrors `MobileV2InviteRequest`
 * on the server. Carries only the invitee ids; the inviter is the authenticated caller.
 */
@Serializable
data class MobileV2InviteRequest(
    val inviteeEmployeeIds: List<String>,
)

/**
 * Wire shape for `POST /api/mobile/v2/flights/{id}/work-orders` — mirrors
 * `MobileCreateWorkOrderRequest` on the server. `clientMutationId` is required
 * for outbox submissions; the server uses it for idempotent retry handling.
 */
@Serializable
data class MobileCreateWorkOrderRequest(
    val flightNumber: String,
    val aircraftTypeId: String?,
    val aircraftTailNumber: String?,
    val ata: String?,
    val atd: String?,
    val remarks: String?,
    val serviceLines: List<MobileWorkOrderServiceLineInput>,
    val tasks: List<MobileWorkOrderTaskInput>,
    val customerSignaturePng: String? = null,
    val clientMutationId: String,
)

/**
 * Wire shape for `POST /api/mobile/v2/flights/{id}/cancel` — mirrors
 * `MobileCancelFlightRequest` on the server. `canceledAt` is the employee-chosen
 * cancellation time; `clientMutationId` is the idempotency key for outbox retries.
 */
@Serializable
data class MobileCancelFlightRequest(
    val canceledAt: String,
    val clientMutationId: String,
)

/**
 * Wire shape for `POST /api/mobile/v2/work-orders/scratch` — mirrors
 * `MobileCreateFromScratchRequest` on the server. `clientFlightId` makes the
 * brand-new flight identifiable across retries; `clientMutationId` does the
 * same for the work order.
 */
@Serializable
data class MobileCreateFromScratchRequest(
    val customerId: String,
    val flightNumber: String,
    val aircraftTypeId: String?,
    val aircraftTailNumber: String?,
    val sta: String,
    val std: String,
    val isCanceled: Boolean,
    val cancellationAt: String?,
    val ata: String?,
    val atd: String?,
    val remarks: String?,
    val serviceLines: List<MobileWorkOrderServiceLineInput>,
    val tasks: List<MobileWorkOrderTaskInput>,
    val customerSignaturePng: String? = null,
    val clientMutationId: String,
    val clientFlightId: String,
)

@Serializable
data class MobileUpdateWorkOrderRequest(
    val flightNumber: String,
    val aircraftTypeId: String?,
    val aircraftTailNumber: String?,
    val ata: String?,
    val atd: String?,
    val remarks: String?,
    val serviceLines: List<MobileWorkOrderServiceLineInput>,
    val tasks: List<MobileWorkOrderTaskInput>,
    /** True when re-submitting an under-review cancel work order with a new cancellation time. */
    val isCanceled: Boolean = false,
    val cancellationAt: String? = null,
    val customerSignaturePng: String? = null,
    val clientMutationId: String? = null,
)

@Serializable
data class MobileReturnToRampRequest(
    val serviceLines: List<MobileWorkOrderServiceLineInput>,
    val tasks: List<MobileWorkOrderTaskInput>,
    val customerSignaturePng: String? = null,
    val clientMutationId: String? = null,
)

@Serializable
data class MobileWorkOrderServiceLineInput(
    val serviceId: String,
    val employeeId: String,
    val from: String,
    val to: String,
    val description: String?,
)

@Serializable
data class MobileWorkOrderTaskInput(
    val taskType: Int,
    val description: String?,
    val from: String,
    val to: String,
    val employeeIds: List<String>,
    val toolIds: List<String>,
    val materialIds: List<String>,
    val generalSupportIds: List<String>,
    val attachments: List<MobileTaskAttachmentInput>,
)

@Serializable
data class MobileTaskAttachmentInput(
    val kind: Int,
    val contentType: String,
    val fileName: String,
    val base64: String,
    val capturedAt: String,
)

/**
 * Response from both create endpoints. [idempotent] is `true` when the server
 * matched the client's mutation/flight id to a prior submission and skipped
 * creating anything new — useful for outbox bookkeeping but invisible to the
 * end user.
 */
@Serializable
data class CreateWorkOrderResponse(
    val id: String,
    val flightId: String,
    val idempotent: Boolean = false,
)
