package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import com.nags.operations.R

/** Performed work is entered explicitly; scheduled services are never marked performed here. */
@Composable
internal fun FlightServiceSection(
    items: List<FlightServiceDraft>,
    catalogs: FlightOperationsCatalogs,
    referenceYear: Int,
    zone: ZoneId,
    onChange: ((List<FlightServiceDraft>) -> List<FlightServiceDraft>) -> Unit,
) {
    var selectingService by rememberSaveable { mutableStateOf<String?>(null) }
    val title = stringResource(R.string.work_services)
    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(stringResource(R.string.work_services_count, items.size), Modifier.padding(horizontal = 14.dp, vertical = 2.dp).semantics { heading() },
            style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.primary)
        items.forEachIndexed { index, item -> key(item.id) {
            FlightWorkEntryGroup {
                val description = item.serviceName.ifBlank { title }
                fun update(change: (FlightServiceDraft) -> FlightServiceDraft) =
                    onChange { current -> current.map { if (it.id == item.id) change(it) else it } }
                FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, description),
                    onRemove = { onChange { current -> current.filterNot { it.id == item.id } } }, compactDelete = true) {
                    FlightWorkChoiceRow(stringResource(R.string.work_service, index + 1),
                        item.serviceName.ifBlank { stringResource(R.string.work_select_service) },
                        stringResource(R.string.work_select_service), isPlaceholder = item.serviceId.isBlank(), leadingPadding = 0.dp,
                        onClick = { selectingService = item.id })
                }
                FlightFormDivider()
                FlightWorkReferenceRow(item.reference, onChange = { reference -> update { it.copy(reference = reference) } })
                FlightFormDivider()
                FlightWorkPeriodFields(item.period, referenceYear, zone, onChange = { period -> update { it.copy(period = period) } })
                FlightFormDivider()
                FlightWorkEmployeeList(item.employees, catalogs.stationStaff, showRemarks = false, referenceYear = referenceYear, zone = zone,
                    onChange = { employees -> update { it.copy(employees = employees) } })
                FlightFormDivider()
                FlightRemarksComposer(item.remarks, onRemarksChange = { remarks -> update { it.copy(remarks = remarks) } },
                    attachments = item.attachments,
                    onAttachmentsChange = { attachments -> update { it.copy(attachments = attachments) } },
                    description = stringResource(R.string.work_service_remarks))
            }
        } }
        FlightFormFieldGroup {
            FlightWorkAddRow(stringResource(R.string.work_add_service, items.size + 1), onClick = {
                val added = FlightServiceDraft(period = FlightWorkPeriod.now(zone))
                onChange { current -> current + added }
            })
        }
    }
    val editing = items.firstOrNull { it.id == selectingService }
    if (editing != null) {
        FlightWorkPicker(title = stringResource(R.string.work_select_service), searchLabel = stringResource(R.string.work_search_services),
            emptyLabel = stringResource(R.string.work_no_services),
            options = catalogs.services.map { FlightWorkChoice(it.id, it.name) }, selectedId = editing.serviceId,
            onSelect = { id ->
                catalogs.services.firstOrNull { it.id == id }?.let { choice ->
                    onChange { current -> current.map { if (it.id == editing.id) it.copy(serviceId = choice.id, serviceName = choice.name) else it } }
                }
            }, onDismiss = { selectingService = null })
    }
}
