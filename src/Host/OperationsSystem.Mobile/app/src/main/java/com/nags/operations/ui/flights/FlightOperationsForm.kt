package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.EditNote
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.PlainTooltip
import androidx.compose.material3.TooltipBox
import androidx.compose.material3.TooltipDefaults
import androidx.compose.material3.rememberTooltipState
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import com.nags.operations.ui.util.formatIsoForDisplay
import java.time.OffsetDateTime
import java.time.LocalDate
import java.time.ZoneId
import java.time.LocalDateTime
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.ArrivalInputError
import com.nags.operations.data.arrivalEditable
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.validationErrors
import com.nags.operations.data.userMessage
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.arrivalInputTime

/** Arrival saves are durable; the remaining operation sections are temporary in this step. */
@Composable
internal fun FlightOperationsForm(
    flight: FlightDetail,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
    catalogs: FlightOperationsCatalogs = FlightOperationsCatalogs(),
    pending: FlightArrivalPendingEntity? = null,
    onSaveArrival: suspend (FlightDetail, SaveFlightArrivalRequest) -> Unit,
    onReviewArrival: suspend () -> FlightDetail,
) {
    val entryTimeZoneId = rememberSaveable { flight.stationTimeZoneId }
    val entryZone = remember(entryTimeZoneId) { stationTimeZone(entryTimeZoneId) }
    val zone = entryZone
    val arrival = flight.visibleArrival()
    val pendingInput = remember(pending?.inputJson) {
        pending?.let { runCatching { Json.decodeFromString<SaveFlightArrivalRequest>(it.inputJson) }.getOrNull() }
    }
    val initialAircraftId = rememberSaveable { pendingInput?.aircraftTypeId ?: arrival?.aircraftTypeId ?: flight.aircraftTypeId.orEmpty() }
    val initialAircraftModel = rememberSaveable { arrival?.aircraftTypeModel ?: flight.aircraftTypeModel.orEmpty() }
    val initialAta = rememberSaveable {
        operationLocalTime(pendingInput?.ata ?: arrival?.ata ?: flight.scheduledArrivalUtc, entryZone)
    }
    val initialAtd = rememberSaveable {
        operationLocalTime(flight.scheduledDepartureUtc, entryZone)
    }
    val referenceYear = rememberSaveable { LocalDate.now(entryZone).year }
    var aircraftId by rememberSaveable { mutableStateOf(initialAircraftId) }
    var ata by rememberSaveable { mutableStateOf(initialAta) }
    var atd by rememberSaveable { mutableStateOf(initialAtd) }
    var tailNumber by rememberSaveable { mutableStateOf(pendingInput?.aircraftTailNumber ?: arrival?.aircraftTailNumber.orEmpty()) }
    var reference by rememberSaveable { mutableStateOf(pendingInput?.customerWorkReferenceNumber ?: arrival?.customerWorkReferenceNumber.orEmpty()) }
    var arrivalRemarks by rememberSaveable { mutableStateOf(pendingInput?.remarks ?: arrival?.remarks.orEmpty()) }
    var departureRemarks by rememberSaveable { mutableStateOf("") }
    var signature by rememberSaveable { mutableStateOf("") }
    var workDrafts by rememberSaveable(stateSaver = FlightWorkDraftsSaver) { mutableStateOf(FlightWorkDrafts()) }
    var originalAta by rememberSaveable { mutableStateOf(pendingInput?.ata ?: arrival?.ata) }
    var arrivalVersion by rememberSaveable { mutableStateOf(pending?.rowVersion ?: flight.rowVersion) }
    fun input() = SaveFlightArrivalRequest(
        arrivalInputTime(ata, originalAta, entryZone),
        aircraftId, tailNumber, reference, arrivalRemarks).normalized()
    var arrivalBaseline by rememberSaveable { mutableStateOf(Json.encodeToString(input())) }
    var queuedBaseline by rememberSaveable { mutableStateOf(pending?.inputJson) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    var saving by remember { mutableStateOf(false) }
    var validationAttempted by rememberSaveable { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    val formScope = rememberCoroutineScope()
    val aircraftAvailable = aircraftTypes.any { it.id.equals(aircraftId, ignoreCase = true) }
    val arrivalChanged = Json.encodeToString(input()) != arrivalBaseline
    val requiresReview = pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId
    val readOnlyArrival = arrival?.takeIf { !flight.arrivalEditable() }
    val arrivalEnabled = flight.arrivalEditable() && !requiresReview && !saving
    val errors = if (validationAttempted && arrivalEnabled) input().validationErrors(arrivalVersion) else emptySet()
    // Refresh untouched arrival fields while retaining edits in every other section.
    LaunchedEffect(arrival, flight.rowVersion, pending?.changeId, pending?.inputJson, pending?.rowVersion) {
        if (pendingInput != null && reviewedChangeId != pending?.changeId) {
            queuedBaseline = pending?.inputJson
            arrivalVersion = pending?.rowVersion.orEmpty()
            if (!arrivalChanged) {
                aircraftId = pendingInput.aircraftTypeId
                originalAta = pendingInput.ata
                ata = operationLocalTime(pendingInput.ata, entryZone)
                tailNumber = pendingInput.aircraftTailNumber
                reference = pendingInput.customerWorkReferenceNumber.orEmpty()
                arrivalRemarks = pendingInput.remarks.orEmpty()
                arrivalBaseline = Json.encodeToString(input())
            }
        } else if (pending == null && queuedBaseline != null) {
            val acknowledged = arrival?.input()?.normalized()?.let { value ->
                value.copy(ata = OffsetDateTime.parse(value.ata).toInstant().toString())
            }
            if (acknowledged != null && Json.encodeToString(acknowledged) == queuedBaseline) {
                // Our queued save was acknowledged while this form was open. Keep any
                // newer edits and advance only to that acknowledged input/version.
                arrivalVersion = flight.rowVersion
                arrivalBaseline = checkNotNull(queuedBaseline)
                queuedBaseline = null
            }
        } else if (!arrivalChanged && pending == null) {
            if (arrival != null) {
                aircraftId = arrival.aircraftTypeId
                originalAta = arrival.ata
                ata = operationLocalTime(arrival.ata, entryZone)
                tailNumber = arrival.aircraftTailNumber
                reference = arrival.customerWorkReferenceNumber.orEmpty()
                arrivalRemarks = arrival.remarks.orEmpty()
                arrivalBaseline = Json.encodeToString(input())
            }
            arrivalVersion = flight.rowVersion
        }
    }
    val mediaSession = rememberFlightMediaSession()
    fun updateWorkDrafts(next: FlightWorkDrafts) {
        val retained = (next.services.flatMap { it.attachments } + next.tasks.flatMap { it.attachments }).map { it.uri }.toSet()
        (workDrafts.services.flatMap { it.attachments } + workDrafts.tasks.flatMap { it.attachments })
            .filter { it.ownedCapture && it.uri !in retained }.forEach { mediaSession.delete(it.uri) }
        workDrafts = next
    }
    val closeForm = { mediaSession.clear(); onClose() }
    val saveArrival = {
        validationAttempted = true
        val value = input()
        if (arrivalEnabled && aircraftAvailable && value.validationErrors(arrivalVersion).isEmpty()) {
            saving = true; saveError = null
            formScope.launch {
                try {
                    onSaveArrival(flight.copy(rowVersion = arrivalVersion), value)
                    arrivalBaseline = Json.encodeToString(value)
                    reviewedChangeId = null
                    closeForm()
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) { saveError = error.userMessage() }
                finally { saving = false }
            }
        }
    }
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    LaunchedEffect(arrivalEnabled) {
        if (!arrivalEnabled && picker in listOf("ata", "aircraft")) picker = null
    }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = arrivalChanged || atd != initialAtd ||
        listOf(departureRemarks, signature).any { it.isNotEmpty() } ||
        workDrafts.services.isNotEmpty() || workDrafts.tasks.isNotEmpty()
    val requestClose = {
        if (saving) Unit else if (picker != null) picker = null else if (hasChanges) confirmDiscard = true else closeForm()
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val unavailable = stringResource(R.string.flights_card_aircraft_unavailable)
    val scheduled = stringResource(R.string.flights_scheduled)
    val aircraftChoices = remember(aircraftTypes, initialAircraftId, initialAircraftModel, unavailable, scheduled) {
        val choices = aircraftTypes.map { ArrivalChoice(it.id, it.model, it.manufacturer) }.toMutableList()
        if (initialAircraftId.isNotBlank() && choices.none { it.id == initialAircraftId }) {
            choices += ArrivalChoice(initialAircraftId, initialAircraftModel.ifBlank { unavailable }, scheduled)
        }
        choices.sortedBy { it.title }
    }
    val selectedAircraft = aircraftChoices.firstOrNull { it.id == aircraftId }?.title
        ?: arrival?.takeIf { it.aircraftTypeId == aircraftId }?.aircraftTypeModel.orEmpty()
    fun openPicker(kind: String) {
        focus.clearFocus(); keyboard?.hide()
        if (kind == "ata" && ata.isBlank()) ata = LocalDateTime.now(zone).withSecond(0).withNano(0).toString()
        if (kind == "atd" && atd.isBlank()) atd = LocalDateTime.now(zone).withSecond(0).withNano(0).toString()
        picker = kind
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.operations_title), stringResource(R.string.operations_save),
        loadCustomerLogo, requestClose,
        saveEnabled = arrivalEnabled && (arrivalChanged || reviewedChangeId != null), saveLoading = saving, onSave = saveArrival) {
        ArrivalSectionHeader(pending, arrival, flight.currentStaffMemberId, entryZone)
        if (pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId) {
            ArrivalFormMessage(stringResource(R.string.arrival_sync_review), error = true)
            pending.error?.takeIf { pending.status != "pending" }?.let { ArrivalFormMessage(it, error = true) }
            if (pending.status != "pending") TextButton(enabled = !saving, onClick = {
                saving = true; saveError = null
                formScope.launch {
                    try {
                        val latest = onReviewArrival()
                        arrivalVersion = latest.rowVersion
                        reviewedChangeId = pending.changeId
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage() }
                    finally { saving = false }
                }
            }) { Text(stringResource(R.string.arrival_reload_review)) }
        } else if (pending != null && pending.status != "pending") ArrivalFormMessage(stringResource(R.string.arrival_review_before_save))
        saveError?.let { ArrivalFormMessage(it, error = true) }
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            FlightFormFieldGroup {
                FlightFormDateTimeField(readOnlyArrival?.let { operationLocalTime(it.ata, entryZone) } ?: ata, stringResource(R.string.arrival_ata_short), stringResource(R.string.arrival_choose_ata),
                    enabled = arrivalEnabled,
                    errorMessage = if (ArrivalInputError.AtaRequired in errors) stringResource(R.string.arrival_ata_required) else null,
                    onClick = { openPicker("ata") })
            }
        }
        if (ArrivalInputError.VersionRequired in errors) ArrivalFormMessage(stringResource(R.string.arrival_reload_required), error = true)
        FlightFormFieldGroup {
            FlightFormChoiceRow(stringResource(R.string.arrival_aircraft), (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).ifBlank { stringResource(R.string.arrival_select_aircraft) },
                stringResource(R.string.arrival_aircraft_type), isPlaceholder = (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).isBlank(), enabled = arrivalEnabled,
                errorMessage = when {
                    ArrivalInputError.AircraftRequired in errors -> stringResource(R.string.arrival_aircraft_required)
                    validationAttempted && arrivalEnabled && !aircraftAvailable -> stringResource(R.string.arrival_aircraft_unavailable)
                    else -> null
                }, onClick = { openPicker("aircraft") })
            FlightFormDivider()
            FlightFormTextRow(stringResource(R.string.arrival_tail_number), readOnlyArrival?.aircraftTailNumber ?: tailNumber, { tailNumber = it },
                stringResource(R.string.arrival_tail_hint), keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next), enabled = arrivalEnabled,
                errorMessage = if (ArrivalInputError.TailRequired in errors) stringResource(R.string.arrival_tail_required) else null)
        }
        FlightFormFieldGroup {
            FlightFormTextRow(stringResource(R.string.arrival_reference), if (readOnlyArrival != null) readOnlyArrival.customerWorkReferenceNumber.orEmpty() else reference, { reference = it }, stringResource(R.string.arrival_optional), enabled = arrivalEnabled)
        }
        FlightFormRemarks(if (readOnlyArrival != null) readOnlyArrival.remarks.orEmpty() else arrivalRemarks, { arrivalRemarks = it }, stringResource(R.string.operations_arrival_remarks), enabled = arrivalEnabled)
        CompositionLocalProvider(LocalFlightMediaSession provides mediaSession) {
            FlightServiceSection(workDrafts.services, catalogs, referenceYear, zone,
                onChange = { change -> updateWorkDrafts(workDrafts.copy(services = change(workDrafts.services))) })
            FlightTaskSection(workDrafts.tasks, catalogs, referenceYear, zone,
                onChange = { change -> updateWorkDrafts(workDrafts.copy(tasks = change(workDrafts.tasks))) })
        }
        OperationsSectionTitle(stringResource(R.string.operations_departure), Modifier.padding(top = 6.dp))
        FlightFormFieldGroup { FlightSignatureField(signature, onOpen = { openPicker("signature") }) }
        FlightFormRemarks(departureRemarks, { departureRemarks = it }, stringResource(R.string.operations_departure_remarks))
        val formFocus = LocalFocusManager.current
        val formKeyboard = LocalSoftwareKeyboardController.current
        Button(onClick = { formFocus.clearFocus(); formKeyboard?.hide(); openPicker("atd") },
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp), shape = RoundedCornerShape(14.dp),
            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp)) {
            Text(stringResource(R.string.operations_submit), style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
        }
    }
    if (picker == "ata" || picker == "atd") {
        val isArrival = picker == "ata"
        FlightFormDateTimeSheet(LocalDateTime.parse(if (isArrival) ata else atd), referenceYear,
            onValueChange = { if (isArrival) ata = it.toString() else atd = it.toString() }, onDismiss = { picker = null },
            title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd), showTitle = !isArrival)
    }
    if (picker == "aircraft") ArrivalAircraftPicker(title = stringResource(R.string.arrival_aircraft_type),
        options = aircraftChoices, selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, { signature = it }, onDismiss = { picker = null })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = closeForm)
}

@Composable
private fun ArrivalFormMessage(message: String, error: Boolean = false) {
    Text(message, Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall,
        color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun OperationsSectionTitle(title: String, modifier: Modifier = Modifier, inset: Boolean = true) {
    Text(title, modifier.padding(horizontal = if (inset) 14.dp else 0.dp).semantics { heading() },
        style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
}

/** Compact status and attribution remain attached to the saved arrival. */
@Composable
private fun ArrivalSectionHeader(pending: FlightArrivalPendingEntity?, arrival: com.nags.operations.data.FlightArrivalSubmission?,
    currentStaffMemberId: String?, zone: ZoneId) {
    val status = when {
        pending != null -> stringResource(R.string.arrival_saved_locally)
        arrival != null -> stringResource(R.string.arrival_published_online)
        else -> null
    }
    val author = arrival?.submittedByEmployee?.takeUnless {
        pending != null || it.staffMemberId.equals(currentStaffMemberId, ignoreCase = true)
    }?.fullName
    val savedAt = arrival?.updatedAtUtc ?: arrival?.createdAtUtc
    val attribution = if (author != null && savedAt != null)
        stringResource(R.string.arrival_author_at, author, formatIsoForDisplay(savedAt, zone)) else null
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 14.dp)) {
            val titleWidth = maxWidth * 0.45f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                ArrivalHeaderTooltip(status) { action ->
                    Row(action.heightIn(min = 24.dp).widthIn(max = titleWidth), horizontalArrangement = Arrangement.spacedBy(6.dp),
                        verticalAlignment = Alignment.CenterVertically) {
                        if (status != null) Icon(if (pending != null) Icons.Outlined.Info else Icons.Filled.CheckCircle,
                            null, Modifier.size(14.dp), tint = if (pending != null) Color(0xFFB85C00) else Color(0xFF248A3D))
                        OperationsSectionTitle(stringResource(R.string.operations_arrival), inset = false)
                    }
                }
                if (attribution != null) ArrivalHeaderTooltip(attribution, Modifier.weight(1f)) { action ->
                    Row(action.fillMaxWidth().heightIn(min = 24.dp), horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.End),
                        verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.EditNote, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        val label = stringResource(R.string.arrival_created_by, author.orEmpty())
                        val nameSize = MaterialTheme.typography.bodySmall.fontSize
                        Text(buildAnnotatedString {
                            append(label)
                            val start = label.indexOf(author.orEmpty())
                            if (start >= 0 && !author.isNullOrEmpty())
                                addStyle(SpanStyle(fontWeight = FontWeight.SemiBold, fontSize = nameSize), start, start + author.length)
                        }, Modifier.weight(1f, fill = false), style = MaterialTheme.typography.labelSmall.copy(fontWeight = FontWeight.Normal),
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ArrivalHeaderTooltip(message: String?, modifier: Modifier = Modifier, content: @Composable (Modifier) -> Unit) {
    if (message == null) {
        content(modifier)
        return
    }
    val state = rememberTooltipState(isPersistent = true)
    val scope = rememberCoroutineScope()
    TooltipBox(positionProvider = TooltipDefaults.rememberTooltipPositionProvider(),
        tooltip = { PlainTooltip { Text(message, style = MaterialTheme.typography.labelSmall) } },
        state = state, modifier = modifier, enableUserInput = false) {
        content(Modifier.clickable(onClickLabel = message, onClick = { scope.launch { state.show() } })
            .semantics(mergeDescendants = true) { contentDescription = message })
    }
}

private data class ArrivalChoice(val id: String, val title: String, val subtitle: String)

@Composable
private fun ArrivalAircraftPicker(title: String, options: List<ArrivalChoice>, selectedId: String,
    onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    var search by rememberSaveable { mutableStateOf("") }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val searchDescription = stringResource(R.string.arrival_search_aircraft)
    val filtered = remember(options, search) {
        options.filter { it.title.contains(search.trim(), ignoreCase = true) || it.subtitle.contains(search.trim(), ignoreCase = true) }
    }
    FlightFormModalSheet(title, onDismiss) { close ->
        Column(Modifier.fillMaxWidth().heightIn(max = 600.dp).fillMaxHeight(0.85f)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(48.dp))
                Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                    Box(Modifier.size(30.dp).background(FlightFormGround, CircleShape), contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(18.dp))
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp).clip(RoundedCornerShape(12.dp))
                .background(FlightFormGround), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.Search, null, Modifier.padding(start = 12.dp, end = 8.dp).size(20.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                BasicTextField(search, { search = it }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)
                    .semantics { contentDescription = searchDescription }, singleLine = true,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Medium),
                    cursorBrush = SolidColor(MaterialTheme.colorScheme.primary), keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { focus.clearFocus(); keyboard?.hide() }),
                    decorationBox = { inner -> Box(contentAlignment = Alignment.CenterStart) {
                        if (search.isEmpty()) Text(stringResource(R.string.arrival_search_aircraft), style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        inner()
                    } })
                if (search.isNotEmpty()) IconButton(onClick = { search = "" }, modifier = Modifier.size(48.dp)) {
                    Icon(Icons.Outlined.Close, stringResource(R.string.flights_search_clear), Modifier.size(18.dp))
                } else Spacer(Modifier.size(12.dp))
            }
            if (filtered.isEmpty()) Text(if (options.isEmpty()) stringResource(R.string.arrival_no_aircraft) else stringResource(R.string.arrival_no_results),
                Modifier.padding(20.dp), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            else LazyColumn(Modifier.weight(1f).fillMaxWidth()) {
                itemsIndexed(filtered, key = { _, option -> option.id }) { index, option ->
                    val selected = option.id == selectedId
                    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).selectable(selected = selected, role = Role.RadioButton,
                        onClick = { onSelect(option.id); focus.clearFocus(); keyboard?.hide(); close() })
                        .padding(horizontal = 20.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(option.title, style = MaterialTheme.typography.bodyLarge,
                                fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            if (option.subtitle.isNotBlank()) Text(option.subtitle, style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        if (selected) Icon(Icons.Filled.Check, stringResource(R.string.arrival_aircraft_selected), Modifier.size(20.dp), tint = MaterialTheme.colorScheme.primary)
                        else Spacer(Modifier.size(20.dp))
                    }
                    if (index < filtered.lastIndex) HorizontalDivider(Modifier.padding(start = 20.dp), thickness = 0.5.dp,
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
                }
            }
        }
    }
}
