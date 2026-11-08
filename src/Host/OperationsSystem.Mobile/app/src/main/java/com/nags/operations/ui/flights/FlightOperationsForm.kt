package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.util.parseInUserZone
import java.time.LocalDate
import java.time.LocalDateTime
import kotlinx.coroutines.flow.Flow

/** Temporary ground-operation entry; submission and attribution are implemented separately later. */
@Composable
internal fun FlightOperationsForm(
    flight: FlightDetail,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
) {
    val initialAircraftId = rememberSaveable { flight.aircraftTypeId.orEmpty() }
    val initialAircraftModel = rememberSaveable { flight.aircraftTypeModel.orEmpty() }
    val initialAta = rememberSaveable {
        runCatching { parseInUserZone(flight.scheduledArrivalUtc).toLocalDateTime().withSecond(0).withNano(0).toString() }.getOrDefault("")
    }
    val initialAtd = rememberSaveable {
        runCatching { parseInUserZone(flight.scheduledDepartureUtc).toLocalDateTime().withSecond(0).withNano(0).toString() }.getOrDefault("")
    }
    val referenceYear = rememberSaveable { LocalDate.now().year }
    var aircraftId by rememberSaveable { mutableStateOf(initialAircraftId) }
    var ata by rememberSaveable { mutableStateOf(initialAta) }
    var atd by rememberSaveable { mutableStateOf(initialAtd) }
    var tailNumber by rememberSaveable { mutableStateOf("") }
    var reference by rememberSaveable { mutableStateOf("") }
    var arrivalRemarks by rememberSaveable { mutableStateOf("") }
    var departureRemarks by rememberSaveable { mutableStateOf("") }
    var signature by rememberSaveable { mutableStateOf("") }
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = aircraftId != initialAircraftId || ata != initialAta || atd != initialAtd ||
        listOf(tailNumber, reference, arrivalRemarks, departureRemarks, signature).any { it.isNotEmpty() }
    val requestClose = {
        if (picker != null) picker = null else if (hasChanges) confirmDiscard = true else onClose()
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
    val selectedAircraft = aircraftChoices.firstOrNull { it.id == aircraftId }?.title.orEmpty()
    fun openPicker(kind: String) {
        focus.clearFocus(); keyboard?.hide()
        if (kind == "ata" && ata.isBlank()) ata = LocalDateTime.now().withSecond(0).withNano(0).toString()
        if (kind == "atd" && atd.isBlank()) atd = LocalDateTime.now().withSecond(0).withNano(0).toString()
        picker = kind
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.operations_title), stringResource(R.string.operations_save),
        loadCustomerLogo, requestClose) {
        OperationsSectionTitle(stringResource(R.string.operations_arrival))
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            FlightFormFieldGroup {
                FlightFormDateTimeField(ata, stringResource(R.string.arrival_ata_short), stringResource(R.string.arrival_choose_ata),
                    onClick = { openPicker("ata") })
            }
            FlightFormTimeZoneHint()
        }
        FlightFormFieldGroup {
            FlightFormChoiceRow(stringResource(R.string.arrival_aircraft), selectedAircraft.ifBlank { stringResource(R.string.arrival_select_aircraft) },
                stringResource(R.string.arrival_aircraft_type), isPlaceholder = selectedAircraft.isBlank(), onClick = { openPicker("aircraft") })
            FlightFormDivider()
            FlightFormTextRow(stringResource(R.string.arrival_tail_number), tailNumber, { tailNumber = it },
                stringResource(R.string.arrival_tail_hint), keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next))
        }
        FlightFormFieldGroup {
            FlightFormTextRow(stringResource(R.string.arrival_reference), reference, { reference = it }, stringResource(R.string.arrival_optional))
        }
        FlightFormRemarks(arrivalRemarks, { arrivalRemarks = it }, stringResource(R.string.operations_arrival_remarks))
        OperationsSectionTitle(stringResource(R.string.operations_departure), Modifier.padding(top = 6.dp))
        FlightFormFieldGroup {
            FlightFormDateTimeField(atd, stringResource(R.string.operations_atd_short), stringResource(R.string.operations_choose_atd),
                onClick = { openPicker("atd") })
        }
        FlightFormFieldGroup { FlightSignatureField(signature, onOpen = { openPicker("signature") }) }
        FlightFormRemarks(departureRemarks, { departureRemarks = it }, stringResource(R.string.operations_departure_remarks))
    }
    if (picker == "ata" || picker == "atd") {
        val isArrival = picker == "ata"
        FlightFormDateTimeSheet(LocalDateTime.parse(if (isArrival) ata else atd), referenceYear,
            onValueChange = { if (isArrival) ata = it.toString() else atd = it.toString() }, onDismiss = { picker = null },
            title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd))
    }
    if (picker == "aircraft") ArrivalAircraftPicker(title = stringResource(R.string.arrival_aircraft_type),
        options = aircraftChoices, selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, { signature = it }, onDismiss = { picker = null })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = onClose)
}

@Composable
private fun OperationsSectionTitle(title: String, modifier: Modifier = Modifier) {
    Text(title, modifier.padding(horizontal = 14.dp).semantics { heading() },
        style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
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
