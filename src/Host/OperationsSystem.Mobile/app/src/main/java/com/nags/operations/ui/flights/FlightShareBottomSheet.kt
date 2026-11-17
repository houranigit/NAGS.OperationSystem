package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
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
import com.nags.operations.ui.theme.AccentBlue
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
    val selected = Json.decodeFromString<List<String>>(selectedJson).toSet()
    var search by rememberSaveable(flight.id) { mutableStateOf("") }
    var messageStep by rememberSaveable(flight.id) { mutableStateOf(false) }
    var attemptJson by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    var attemptedFlight by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    var busy by remember(flight.id) { mutableStateOf(false) }
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true,
        confirmValueChange = { !busy || it != SheetValue.Hidden })
    var error by rememberSaveable(flight.id) { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    var messageFocused by remember { mutableStateOf(false) }
    var messageBounds by remember { mutableStateOf<Rect?>(null) }
    var sheetOrigin by remember { mutableStateOf(Offset.Zero) }
    val frozen = attemptJson != null
    val composing = messageStep || frozen
    val canPublish = online && !busy && (frozen || flight.canShare && !flight.hasCancellationSubmission) && message.length <= 2000
    fun removeEmployee(id: String) {
        if (!busy && !frozen) selectedJson = Json.encodeToString((selected - id).toList())
    }
    val title = if (composing) stringResource(R.string.flight_share_title)
        else stringResource(R.string.flight_share_station_staff, flight.stationIata)
    val ground = flightPickerPageBackground()
    val card = remember(flight, listItem) { flight.formCardSummary(listItem, emptyList()).withReturnToRampReports(flight) }
    val stationCount = remember(stationStaff, flight.stationId) {
        stationStaff.filter { it.stationId.equals(flight.stationId, true) }.distinctBy { it.id }.size
    }
    ModalBottomSheet(onDismissRequest = { if (!busy) onDismiss() }, sheetState = sheetState,
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp), containerColor = ground,
        sheetGesturesEnabled = !composing || !messageFocused,
        dragHandle = null, tonalElevation = 0.dp, scrimColor = Color.Black.copy(alpha = 0.28f)) {
        val focus = LocalFocusManager.current
        val keyboard = LocalSoftwareKeyboardController.current
        fun changeStep(showMessage: Boolean) {
            if (busy || frozen) return
            focus.clearFocus()
            keyboard?.hide()
            messageStep = showMessage
        }
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
            focus.clearFocus()
            keyboard?.hide()
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
        BackHandler(enabled = composing && !busy && !frozen) { changeStep(false) }
        Column(Modifier.fillMaxWidth().fillMaxHeight(0.94f).imePadding().semantics { paneTitle = title }
            .onGloballyPositioned { sheetOrigin = it.positionInWindow() }
            .pointerInput(focus, keyboard) {
                // Observe taps after child controls, without turning scrolls into keyboard dismissals.
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                    var isTap = true
                    var release = down.position
                    do {
                        val event = awaitPointerEvent(PointerEventPass.Final)
                        if (event.changes.size > 1 || event.changes.any {
                            (it.position - down.position).getDistance() > viewConfiguration.touchSlop
                        }) isTap = false
                        event.changes.firstOrNull { it.id == down.id }?.let { release = it.position }
                    } while (event.changes.any { it.pressed })
                    val bounds = messageBounds
                    if (messageFocused && isTap && bounds != null && !bounds.contains(release + sheetOrigin)) {
                        focus.clearFocus()
                        keyboard?.hide()
                    }
                }
            }) {
            Box(Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(horizontal = 16.dp, vertical = 8.dp)) {
                IconButton(onClick = { if (composing && !frozen) changeStep(false) else onDismiss() },
                    enabled = !busy, modifier = Modifier.size(48.dp).align(Alignment.CenterStart)) {
                    Surface(shape = CircleShape, color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f)) {
                        Box(Modifier.size(44.dp), contentAlignment = Alignment.Center) {
                            Icon(if (composing && !frozen) Icons.AutoMirrored.Outlined.ArrowBack else Icons.Outlined.Close,
                                stringResource(if (composing && !frozen) R.string.account_back else R.string.flight_form_close_picker), Modifier.size(24.dp))
                        }
                    }
                }
                Column(Modifier.align(Alignment.Center).fillMaxWidth().padding(horizontal = 88.dp),
                    horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(buildAnnotatedString {
                        append(title)
                        if (!composing && flight.stationIata.isNotBlank()) {
                            val stationStart = title.indexOf(flight.stationIata)
                            if (stationStart >= 0) addStyle(SpanStyle(fontWeight = FontWeight.ExtraBold),
                                stationStart, stationStart + flight.stationIata.length)
                        }
                    }, style = MaterialTheme.typography.titleMedium.copy(fontSize = 15.sp, lineHeight = 19.sp),
                        fontWeight = if (composing) FontWeight.SemiBold else FontWeight.Normal,
                        textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    if (!composing) Text(if (selected.isEmpty()) stringResource(R.string.flight_share_all_staff)
                        else stringResource(R.string.flight_share_selection_count, selected.size, stationCount),
                        style = MaterialTheme.typography.labelSmall.copy(fontSize = 11.sp, lineHeight = 14.sp), textAlign = TextAlign.Center)
                }
                Box(Modifier.align(Alignment.CenterEnd).heightIn(min = 48.dp), contentAlignment = Alignment.Center) {
                    Button(onClick = { if (composing) publish() else changeStep(true) },
                        enabled = if (composing) canPublish else !busy,
                        modifier = Modifier.defaultMinSize(minWidth = 56.dp).heightIn(min = 32.dp),
                        shape = CircleShape, contentPadding = PaddingValues(horizontal = 12.dp, vertical = 0.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = if (composing) AccentBlue else FlightPickerActionGreen, contentColor = Color.White)) {
                        if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = Color.White)
                        else Text(stringResource(if (!composing) R.string.flight_share_next else if (frozen) R.string.flight_share_retry else R.string.flight_share_publish),
                            style = MaterialTheme.typography.labelMedium.copy(fontSize = 13.sp, lineHeight = 16.sp), fontWeight = FontWeight.SemiBold)
                    }
                }
            }
            AnimatedContent(targetState = composing, modifier = Modifier.weight(1f), label = "Share step",
                transitionSpec = {
                    val direction = if (targetState) 1 else -1
                    (slideInHorizontally(tween(220)) { it * direction } + fadeIn(tween(160))) togetherWith
                        (slideOutHorizontally(tween(220)) { -it * direction } + fadeOut(tween(120)))
                }) { showMessage ->
                if (!showMessage) FlightShareEmployeePicker(stationStaff = stationStaff, stationId = flight.stationId,
                    selected = selected, search = search, onSearchChange = { search = it },
                    onToggle = { id -> selectedJson = Json.encodeToString((if (id in selected) selected - id else selected + id).toList()) },
                    enabled = !busy && !frozen, modifier = Modifier.fillMaxSize())
                else CompositionLocalProvider(LocalFlightFormCompact provides true) {
                    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalArrangement = Arrangement.spacedBy(20.dp)) {
                        val messageFocus = remember { FocusRequester() }
                        LaunchedEffect(Unit) { if (!frozen) messageFocus.requestFocus() }
                        FlightCard(card, onClick = null, loadCustomerLogo = loadCustomerLogo, reportStatus = reportStatus, compact = true)
                        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            FlightFormSectionTitle(stringResource(R.string.flight_share_message_title))
                            Surface(modifier = Modifier.fillMaxWidth().onGloballyPositioned { messageBounds = it.boundsInWindow() },
                                shape = RoundedCornerShape(24.dp), color = flightPickerCardBackground()) {
                                FlightFormTextInput(message, { if (it.length <= 2000) message = it },
                                    placeholder = stringResource(R.string.flight_share_message), label = stringResource(R.string.flight_share_message),
                                    modifier = Modifier.padding(16.dp).focusRequester(messageFocus)
                                        .onFocusChanged { messageFocused = it.hasFocus }, multiline = true, textAlign = TextAlign.Left,
                                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done), enabled = !busy && !frozen)
                            }
                        }
                        if (!online) InlineNotice(stringResource(R.string.flight_share_online_required), kind = InlineNoticeKind.Warning)
                        error?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
                        if (frozen && !busy) InlineNotice(stringResource(R.string.flight_share_retry_hint), kind = InlineNoticeKind.Warning)
                        Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            FlightFormSectionTitle(stringResource(R.string.flight_share_list_title))
                            FlightShareSelectedEmployees(stationStaff, selected, ::removeEmployee, enabled = !busy && !frozen,
                                modifier = Modifier.fillMaxWidth().background(flightPickerCardBackground(), RoundedCornerShape(16.dp)),
                                onAdd = { changeStep(false) }, wrap = true)
                            if (selected.isEmpty()) Text(stringResource(R.string.flight_share_whole_station_note),
                                Modifier.fillMaxWidth().padding(horizontal = 14.dp),
                                style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
    }
}
