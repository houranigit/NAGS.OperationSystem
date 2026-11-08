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
import com.nags.operations.R

/** Performed work is entered explicitly; scheduled services are never marked performed here. */
@Composable
internal fun FlightServiceSection(
    items: List<FlightServiceDraft>,
    catalogs: FlightOperationsCatalogs,
    referenceYear: Int,
    onChange: (List<FlightServiceDraft>) -> Unit,
) {
    var selectingService by rememberSaveable { mutableStateOf<String?>(null) }
    val title = stringResource(R.string.work_services)
    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, Modifier.padding(horizontal = 14.dp, vertical = 2.dp).semantics { heading() },
            style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.primary)
        FlightFormFieldGroup {
            items.forEach { item -> key(item.id) {
                val description = item.serviceName.ifBlank { title }
                fun update(updated: FlightServiceDraft) = onChange(items.map { if (it.id == item.id) updated else it })
                FlightWorkRemovableEntry(item.id, stringResource(R.string.work_remove_item, description),
                    onRemove = { onChange(items.filterNot { it.id == item.id }) }) {
                    FlightWorkChoiceRow(stringResource(R.string.work_service),
                        item.serviceName.ifBlank { stringResource(R.string.work_select_service) },
                        stringResource(R.string.work_select_service), isPlaceholder = item.serviceId.isBlank(),
                        onClick = { selectingService = item.id })
                    FlightFormDivider()
                    FlightWorkPeriodFields(item.period, referenceYear, onChange = { update(item.copy(period = it)) })
                    FlightWorkNotes(item.reference, { update(item.copy(reference = it)) },
                    item.remarks, { update(item.copy(remarks = it)) }, stringResource(R.string.work_service_remarks))
                }
                FlightFormDivider()
                FlightWorkEmployeeList(item.employees, catalogs.stationStaff, showRemarks = false, referenceYear = referenceYear,
                    onChange = { update(item.copy(employees = it)) })
                FlightFormDivider()
                FlightWorkAttachments(item.attachments, onChange = { update(item.copy(attachments = it)) })
                FlightFormDivider()
            } }
            FlightWorkAddRow(stringResource(R.string.work_add_service), onClick = { onChange(items + FlightServiceDraft()) })
        }
    }
    val editing = items.firstOrNull { it.id == selectingService }
    if (editing != null) {
        FlightWorkPicker(title = stringResource(R.string.work_select_service), searchLabel = stringResource(R.string.work_search_services),
            emptyLabel = stringResource(R.string.work_no_services),
            options = catalogs.services.map { FlightWorkChoice(it.id, it.name) }, selectedId = editing.serviceId,
            onSelect = { id ->
                catalogs.services.firstOrNull { it.id == id }?.let { choice ->
                    onChange(items.map { if (it.id == editing.id) it.copy(serviceId = choice.id, serviceName = choice.name) else it })
                }
            }, onDismiss = { selectingService = null })
    }
}
