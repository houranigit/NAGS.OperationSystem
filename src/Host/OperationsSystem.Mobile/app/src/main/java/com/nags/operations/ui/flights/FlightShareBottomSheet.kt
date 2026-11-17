package com.nags.operations.ui.flights

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.userMessage
import com.nags.operations.data.withReturnToRampReports
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.ui.theme.LoadingIndicatorGray
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

internal fun FlightDetail.canShowShareAction(hasPendingCancellation: Boolean = false): Boolean =
    canShare && !hasCancellationSubmission && !hasPendingCancellation

/** One publishing attempt retains its identity and normalized body until its result is known. */
@Serializable
internal data class FlightShareAttempt(val id: String, val message: String?, val staffMemberIds: List<String>) {
    companion object {
        fun create(id: String, message: String, recipients: Collection<String>) = FlightShareAttempt(id,
            message.trim().takeIf { it.isNotEmpty() }, recipients.distinct().sorted())
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightShareBottomSheet(flight: FlightDetail, stationStaff: List<StaffCatalogItem>, online: Boolean,
    listItem: FlightListItem?, reportStatus: FlightCardReportStatus,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onDismiss: () -> Unit,
    onPublish: suspend (FlightDetail, String, String?, List<String>) -> Unit,
) {
    var message by rememberSaveable(flight.id) { mutableStateOf("") }
    var selectedJson by rememberSaveable(flight.id) { mutableStateOf("[]") }
    val selected = Json.decodeFromString<List<String>>(selectedJson)
    var search by rememberSaveable(flight.id) { mutableStateOf("") }
    var attemptJson by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    var attemptedFlight by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    var busy by remember(flight.id) { mutableStateOf(false) }
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true,
        confirmValueChange = { !busy || it != SheetValue.Hidden })
    var error by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    val frozen = attemptJson != null
    val canPublish = online && !busy && (frozen || flight.canShare && !flight.hasCancellationSubmission) && message.length <= 2000
    fun publish() {
        if (!canPublish) return
        val attempt = attemptJson?.let { Json.decodeFromString<FlightShareAttempt>(it) }
            ?: FlightShareAttempt.create(UUID.randomUUID().toString(), message, selected).also {
                attemptJson = Json.encodeToString(it)
                attemptedFlight = Json.encodeToString(flight)
            }
        val requestFlight = attemptedFlight?.let { Json.decodeFromString<FlightDetail>(it) } ?: flight
        busy = true
        error = null
        scope.launch {
            try {
                onPublish(requestFlight, attempt.id, attempt.message, attempt.staffMemberIds)
                busy = false
                sheetState.hide()
                onDismiss()
            } catch (cancelled: CancellationException) {
                busy = false
                throw cancelled
            } catch (failure: Exception) {
                // A definite 4xx rejection did not publish this body. Transport/5xx outcomes remain frozen.
                if (failure is ApiException && failure.statusCode in 400..499 && failure.statusCode !in setOf(408, 429)) {
                    attemptJson = null
                    attemptedFlight = null
                }
                error = failure.userMessage()
                busy = false
            }
        }
    }
    val title = stringResource(R.string.flight_share_title)
    val ground = if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color.White else MaterialTheme.colorScheme.surface
    val card = remember(flight, listItem) { flight.formCardSummary(listItem, emptyList()).withReturnToRampReports(flight) }
    ModalBottomSheet(onDismissRequest = { if (!busy) onDismiss() }, sheetState = sheetState,
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp), containerColor = ground,
        tonalElevation = 0.dp, scrimColor = Color.Black.copy(alpha = 0.28f)) {
        Column(Modifier.fillMaxWidth().fillMaxHeight(0.94f).imePadding().semantics { paneTitle = title }) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                TextButton(onClick = ::publish, enabled = canPublish) {
                    if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = LoadingIndicatorGray)
                    else Text(stringResource(if (frozen) R.string.flight_share_retry else R.string.flight_share_publish))
                }
            }
            FlightShareEmployeePicker(stationStaff = stationStaff, stationId = flight.stationId,
                selected = selected.toSet(), search = search, onSearchChange = { search = it },
                onToggle = { id -> selectedJson = Json.encodeToString(if (id in selected) selected - id else selected + id) },
                enabled = !busy && !frozen, modifier = Modifier.weight(1f), headerContent = {
                    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        FlightCard(card, onClick = null, loadCustomerLogo = loadCustomerLogo, reportStatus = reportStatus, compact = true)
                        Surface(shape = RoundedCornerShape(14.dp), color = ground,
                            border = BorderStroke(0.5.dp, MaterialTheme.colorScheme.outlineVariant)) {
                            FlightFormRemarks(message, { if (it.length <= 2000) message = it },
                                description = stringResource(R.string.flight_share_message),
                                heading = stringResource(R.string.flight_share_message), enabled = !busy && !frozen, inGroup = true)
                        }
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically) {
                            Text(if (selected.isEmpty()) stringResource(R.string.flight_share_whole_station)
                                else stringResource(R.string.flight_share_selected_count, selected.size),
                                modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall)
                            if (selected.isNotEmpty()) TextButton(onClick = { selectedJson = "[]" }, enabled = !busy && !frozen) {
                                Text(stringResource(R.string.flight_share_clear_recipients))
                            }
                        }
                        if (!online) InlineNotice(stringResource(R.string.flight_share_online_required), kind = InlineNoticeKind.Warning)
                        error?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
                        if (frozen && !busy) InlineNotice(stringResource(R.string.flight_share_retry_hint), kind = InlineNoticeKind.Warning)
                    }
                })
        }
    }
}
