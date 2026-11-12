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
    canChangeService: (String) -> Boolean = { true },
    errors: Map<String, Map<String, Int>> = emptyMap(),
    defaultEmployee: FlightEmployeeDraft? = null,
    timeBounds: FlightWorkTimeBounds = FlightWorkTimeBounds(),
    showHeading: Boolean = true,
    showAdd: Boolean = true,
    countOffset: Int = 0,
    showRemoveControls: Boolean = true,
    draftLabels: Boolean = false,
) {
    var selectingService by rememberSaveable { mutableStateOf<String?>(null) }
    var addingService by rememberSaveable { mutableStateOf(false) }
    var selectionHandled by rememberSaveable { mutableStateOf(false) }
    val title = stringResource(R.string.work_services)
    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (showHeading) FlightFormSectionTitle(stringResource(R.string.work_services_count, items.size + countOffset))
        items.forEach { item -> key(item.id) {
            var initialStartStaffIds by rememberSaveable { mutableStateOf(emptyList<String>()) }
            val fields = errors[item.id].orEmpty()
            val defaultStaffReady = item.plannedServiceId == null || item.period.start.isNotBlank() ||
                item.employees.isNotEmpty() || defaultEmployee != null
            val editable = enabled && defaultStaffReady && canEdit(item.id)
            val endEditable = enabled && defaultStaffReady && canSetEnd(item.id)
            FlightWorkEntryGroup(Modifier.flightFormNavigationTarget("service:${item.id}", highlight = true)) {
                if (draftLabels) FlightWorkDraftLabel(stringResource(R.string.work_new_service_draft),
                    badge = stringResource(R.string.work_scheduled_service).takeIf {
                        item.plannedServiceId?.takeIf(String::isNotBlank)?.equals(item.serviceId, ignoreCase = true) == true
                    })
                val description = item.serviceName.ifBlank { title }
                fun update(change: (FlightServiceDraft) -> FlightServiceDraft) =
                    onChange { current -> current.map { if (it.id == item.id) change(it) else it } }
                FlightWorkOptionalRemoval(showRemoveControls, item.id, stringResource(R.string.work_remove_item, description),
                    onRemove = { onChange { current -> current.filterNot { it.id == item.id } } }, compactDelete = true, enabled = enabled && canDelete(item.id)) {
                    FlightWorkChoiceRow("",
                        item.serviceName.ifBlank { stringResource(R.string.flight_form_required_field) },
                        stringResource(R.string.work_select_service), isPlaceholder = item.serviceId.isBlank(), leadingPadding = 0.dp,
                        enabled = editable && canChangeService(item.id),
                        errorMessage = fields["serviceId"]?.let { stringResource(it) },
                        onClick = { selectingService = item.id; addingService = false; selectionHandled = false })
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
                    endRequired = timeBounds.upperUtc != null,
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
                    label = stringResource(R.string.work_service_remarks_heading),
                    required = item.serviceId.equals("40000000-0000-0000-0000-000000000003", ignoreCase = true),
                    errorMessage = (fields["notes"] ?: fields["attachments"])?.let { stringResource(it) })
            }
        } }
        if (showAdd) FlightFormFieldGroup {
            FlightWorkAddRow(stringResource(R.string.work_add_service, items.size + countOffset + 1),
                enabled = enabled && defaultEmployee != null && catalogs.services.any { !it.isAircraftPerLanding }, onClick = {
                selectingService = null
                addingService = true
                selectionHandled = false
            })
        }
    }
    val editing = items.firstOrNull { it.id == selectingService }
    if (enabled && (addingService && defaultEmployee != null || editing != null && canEdit(editing.id) && canChangeService(editing.id))) {
        FlightWorkPicker(title = stringResource(R.string.work_select_service), searchLabel = stringResource(R.string.work_search_services),
            emptyLabel = stringResource(R.string.work_no_services),
            options = catalogs.services.filterNot { it.isAircraftPerLanding }.map { FlightWorkChoice(it.id, it.name) }, selectedId = editing?.serviceId,
            onSelect = { id ->
                if (!selectionHandled) catalogs.services.firstOrNull { it.id == id && !it.isAircraftPerLanding }?.let { choice ->
                    selectionHandled = true
                    if (addingService) {
                        val period = seedBlankWorkStart(FlightWorkPeriod(), zone, timeBounds)
                        val added = FlightServiceDraft(serviceId = choice.id, serviceName = choice.name, period = period,
                            employees = listOfNotNull(defaultEmployee?.copy(id = UUID.randomUUID().toString(), period = period)))
                        onChange { current -> current + added }
                    } else if (editing != null) {
                        onChange { current -> current.map { if (it.id == editing.id) it.copy(serviceId = choice.id, serviceName = choice.name) else it } }
                    }
                }
            }, onDismiss = { selectingService = null; addingService = false })
    }
}
