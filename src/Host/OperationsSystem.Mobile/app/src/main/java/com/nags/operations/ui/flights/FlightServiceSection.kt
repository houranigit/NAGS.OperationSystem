package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import java.util.UUID
import com.nags.operations.R

/** Planned placeholders stay drafts until started; only authorized fields can change saved work. */
@Composable
internal fun FlightServiceSection(
    items: List<FlightServiceDraft>,
    catalogs: FlightOperationsCatalogs,
    referenceYear: Int,
    zone: ZoneId,
    onChange: ((List<FlightServiceDraft>) -> List<FlightServiceDraft>) -> Unit,
    enabled: Boolean = true,
    canEdit: (String) -> Boolean = { true },
    canSetEnd: (String) -> Boolean = { true },
    canDelete: (String) -> Boolean = { true },
    errors: Map<String, Map<String, Int>> = emptyMap(),
    defaultEmployee: FlightEmployeeDraft? = null,
    timeBounds: FlightWorkTimeBounds = FlightWorkTimeBounds(),
) {
    var selectingService by rememberSaveable { mutableStateOf<String?>(null) }
    val title = stringResource(R.string.work_services)
    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        FlightFormSectionTitle(stringResource(R.string.work_services_count, items.size))
        items.forEachIndexed { index, item -> key(item.id) {
            var initialStartStaffIds by rememberSaveable { mutableStateOf(emptyList<String>()) }
            val fields = errors[item.id].orEmpty()
            val defaultStaffReady = item.plannedServiceId == null || item.period.start.isNotBlank() ||
                item.employees.isNotEmpty() || defaultEmployee != null
            val editable = enabled && defaultStaffReady && canEdit(item.id)
            val endEditable = enabled && defaultStaffReady && canSetEnd(item.id)
            FlightWorkEntryGroup {
                val description = item.serviceName.ifBlank { title }
                fun update(change: (FlightServiceDraft) -> FlightServiceDraft) =
                    onChange { current -> current.map { if (it.id == item.id) change(it) else it } }
                FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, description),
                    onRemove = { onChange { current -> current.filterNot { it.id == item.id } } }, compactDelete = true, enabled = enabled && canDelete(item.id)) {
                    FlightWorkChoiceRow(stringResource(R.string.work_service, index + 1),
                        item.serviceName.ifBlank { stringResource(R.string.work_select_service) },
                        stringResource(R.string.work_select_service), isPlaceholder = item.serviceId.isBlank(), leadingPadding = 0.dp, enabled = editable,
                        errorMessage = fields["serviceId"]?.let { stringResource(it) },
                        onClick = { selectingService = item.id })
                }
                FlightFormDivider()
                FlightWorkReferenceRow(item.reference, onChange = { reference -> update { it.copy(reference = reference) } },
                    enabled = editable, errorMessage = fields["customerReferenceNumber"]?.let { stringResource(it) })
                FlightFormDivider()
                FlightWorkPeriodFields(item.period, referenceYear, zone, onChange = { period ->
                    update { previous -> servicePeriodWithInitialStaff(previous, period, initialStartStaffIds.toSet()) }
                }, startEnabled = editable, endEnabled = endEditable,
                    startError = fields["startUtc"]?.let { stringResource(it) },
                    endError = fields["endUtc"]?.let { stringResource(it) }, seedEndNow = true, timeBounds = timeBounds,
                    onStartPickerSessionChanged = { initialBlank -> initialStartStaffIds =
                        if (initialBlank) item.employees.filter { it.period.start.isBlank() }.map { it.id } else emptyList() })
                FlightFormDivider()
                FlightWorkEmployeeList(item.employees, catalogs.stationStaff, showRemarks = false, referenceYear = referenceYear, zone = zone,
                    onChange = { employees -> update { it.copy(employees = employees) } }, enabled = editable,
                    errorMessage = fields["performers"]?.let { stringResource(it) }, fieldErrors = fields,
                    timeBounds = timeBounds.within(item.period, zone))
                FlightFormDivider()
                FlightRemarksComposer(item.remarks, onRemarksChange = { remarks -> update { it.copy(remarks = remarks) } },
                    attachments = item.attachments,
                    onAttachmentsChange = { attachments -> update { it.copy(attachments = attachments) } },
                    description = stringResource(R.string.work_service_remarks), enabled = editable,
                    errorMessage = (fields["notes"] ?: fields["attachments"])?.let { stringResource(it) })
            }
        } }
        FlightFormFieldGroup {
            FlightWorkAddRow(stringResource(R.string.work_add_service, items.size + 1),
                enabled = enabled && defaultEmployee != null && catalogs.services.any { !it.isAircraftPerLanding }, onClick = {
                val period = seedBlankWorkStart(FlightWorkPeriod(), zone, timeBounds)
                val added = FlightServiceDraft(period = period,
                    employees = listOfNotNull(defaultEmployee?.copy(id = UUID.randomUUID().toString(), period = period)))
                onChange { current -> current + added }
            })
        }
    }
    val editing = items.firstOrNull { it.id == selectingService }
    if (editing != null && enabled && canEdit(editing.id)) {
        FlightWorkPicker(title = stringResource(R.string.work_select_service), searchLabel = stringResource(R.string.work_search_services),
            emptyLabel = stringResource(R.string.work_no_services),
            options = catalogs.services.filterNot { it.isAircraftPerLanding }.map { FlightWorkChoice(it.id, it.name) }, selectedId = editing.serviceId,
            onSelect = { id ->
                catalogs.services.firstOrNull { it.id == id }?.let { choice ->
                    onChange { current -> current.map { if (it.id == editing.id) it.copy(serviceId = choice.id, serviceName = choice.name) else it } }
                }
            }, onDismiss = { selectingService = null })
    }
}
