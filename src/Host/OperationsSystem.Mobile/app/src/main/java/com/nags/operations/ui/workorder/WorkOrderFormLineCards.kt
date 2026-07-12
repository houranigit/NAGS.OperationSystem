package com.nags.operations.ui.workorder

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.data.TaskTypeKind
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.workOrderPickerDisplayLine
import com.nags.operations.ui.components.DocumentAttachmentButton
import com.nags.operations.ui.components.MultiSelectDropdownField
import com.nags.operations.ui.components.PhotoAttachmentButton
import com.nags.operations.ui.components.TaskAttachmentRow
import com.nags.operations.ui.components.VoiceAttachmentButton
import com.nags.operations.ui.components.WorkOrderDateTimePickerField
import com.nags.operations.ui.components.WorkOrderEmployeePicker
import com.nags.operations.ui.components.WorkOrderServicePicker
import com.nags.operations.ui.components.formatMultiSelectSummary
import java.time.ZoneOffset

private fun resolvedServiceName(row: ServiceLineFormRow, services: List<ServiceEntity>): String? =
    services.firstOrNull { it.serviceId == row.serviceId }?.name

private fun serviceRecapChipText(row: ServiceLineFormRow, services: List<ServiceEntity>): String =
    resolvedServiceName(row, services) ?: "Needs service"

fun idsPreservingCatalogOrder(selection: Set<String>, catalogOrderedIds: List<String>): List<String> =
    catalogOrderedIds.filter { it in selection }

@Composable
fun fieldErrorSupportingText(message: String?): (@Composable () -> Unit)? =
    message?.takeIf { it.isNotBlank() }?.let { m ->
        { Text(m, style = MaterialTheme.typography.bodySmall) }
    }

private fun taskRecapChipText(row: TaskFormRow): String {
    val typePart = TaskTypeKind.label(row.taskType)
    val desc = row.description.trim()
    if (desc.isEmpty()) return "$typePart · Add details"
    val snippet = desc.take(40)
    val suffix = if (desc.length > 40) "…" else ""
    return "$typePart · $snippet$suffix"
}

@Composable
fun FormSectionTitle(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleMedium,
        fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(top = 8.dp),
    )
}

@Composable
fun TasksSectionHeading(
    catalogsMissingEmployees: Boolean,
    catalogsMissingTools: Boolean,
    catalogsMissingMaterials: Boolean,
    catalogsMissingGeneralSupports: Boolean,
) {
    val missingLabels = buildList {
        if (catalogsMissingEmployees) add("Employees")
        if (catalogsMissingTools) add("Tools")
        if (catalogsMissingMaterials) add("Materials")
        if (catalogsMissingGeneralSupports) add("General supports")
    }
    if (missingLabels.isEmpty()) return
    Text(
        text = "${missingLabels.joinToString(", ")} — " +
            if (missingLabels.size == 1) {
                "this catalog is empty. Sync from the flight list or Sync Center."
            } else {
                "these catalogs are empty. Sync from the flight list or Sync Center."
            },
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.tertiary,
        modifier = Modifier.padding(top = 4.dp),
    )
}

@Composable
fun ServiceLinesSectionHeading(
    catalogsMissingServices: Boolean,
    catalogsMissingEmployees: Boolean,
) {
    Column(Modifier.fillMaxWidth()) {
        Text(
            text = "Services",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(top = 8.dp),
        )
        if (catalogsMissingServices || catalogsMissingEmployees) {
            val hint = when {
                catalogsMissingServices && catalogsMissingEmployees ->
                    "Service and employee catalogs are empty — sync from the flight list or Sync Center."
                catalogsMissingServices ->
                    "Services catalog is empty — sync before you can tag a service type."
                else ->
                    "Employees catalog is empty — sync before you can choose who performed services."
            }
            Text(
                text = hint,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.tertiary,
                modifier = Modifier.padding(top = 8.dp),
            )
        }
    }
}

@Composable
fun WorkOrderLineRecapChip(
    lineNumber: Int,
    chipLabel: String,
    emphasize: Boolean,
    showReturnToRamp: Boolean = false,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(percent = 50),
        color = if (emphasize) {
            MaterialTheme.colorScheme.secondaryContainer
        } else {
            MaterialTheme.colorScheme.surfaceContainerHighest
        },
    ) {
        Row(
            Modifier
                .padding(horizontal = 12.dp, vertical = 9.dp)
                .fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (showReturnToRamp) {
                Surface(
                    shape = RoundedCornerShape(percent = 50),
                    color = MaterialTheme.colorScheme.tertiaryContainer,
                ) {
                    Text(
                        text = "RTR",
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onTertiaryContainer,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                    )
                }
            }
            Surface(
                shape = RoundedCornerShape(percent = 50),
                color = MaterialTheme.colorScheme.primary,
            ) {
                Text(
                    text = "$lineNumber",
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                )
            }
            Text(
                text = chipLabel,
                style = MaterialTheme.typography.bodyMedium,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ServiceLineCard(
    lineNumber: Int,
    flightOffset: ZoneOffset,
    scheduleAnchorIso: String,
    row: ServiceLineFormRow,
    lineErrors: ServiceLineSubmitFieldErrors?,
    services: List<ServiceEntity>,
    employees: List<EmployeeEntity>,
    onChange: (ServiceLineFormRow) -> Unit,
    onRemove: () -> Unit,
    canRemove: Boolean,
) {
    val hasServiceSelected = resolvedServiceName(row, services) != null
    val chipLabel = remember(row.serviceId, services) {
        serviceRecapChipText(row, services)
    }

    val fieldShape = RoundedCornerShape(14.dp)
    val radius = 14.dp

    OutlinedCard(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(radius),
        colors = CardDefaults.outlinedCardColors(),
    ) {
        Row(
            Modifier
                .fillMaxWidth()
                .height(IntrinsicSize.Min),
        ) {
            Spacer(
                modifier = Modifier
                    .fillMaxHeight()
                    .width(5.dp)
                    .background(
                        color = MaterialTheme.colorScheme.primary,
                        shape = RoundedCornerShape(
                            topStart = radius,
                            bottomStart = radius,
                        ),
                    ),
            )
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(start = 12.dp, end = 14.dp, top = 14.dp, bottom = 14.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Row(
                    Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    WorkOrderLineRecapChip(
                        lineNumber = lineNumber,
                        chipLabel = chipLabel,
                        emphasize = hasServiceSelected,
                        showReturnToRamp = row.returnToRamp,
                        modifier = Modifier.weight(1f),
                    )
                    if (canRemove) {
                        IconButton(onClick = onRemove) {
                            Icon(
                                Icons.Default.DeleteOutline,
                                contentDescription = "Remove service line",
                                tint = MaterialTheme.colorScheme.error,
                            )
                        }
                    }
                }

                WorkOrderServicePicker(
                    selectedId = row.serviceId,
                    options = services,
                    onSelected = { svc ->
                        onChange(row.copy(serviceId = svc.serviceId))
                    },
                    isError = lineErrors?.serviceType != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.serviceType),
                )
                WorkOrderEmployeePicker(
                    selectedId = row.employeeId,
                    options = employees,
                    onSelected = { emp ->
                        onChange(row.copy(employeeId = emp.staffMemberId))
                    },
                    isError = lineErrors?.performer != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.performer),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    WorkOrderDateTimePickerField(
                        iso = row.fromIso,
                        label = "From",
                        placeholder = "Start date & time",
                        flightOffset = flightOffset,
                        defaultInitialIso = scheduleAnchorIso,
                        onIsoConfirmed = { onChange(row.copy(fromIso = it)) },
                        modifier = Modifier.weight(1f),
                        isError = lineErrors?.from != null,
                        supportingText = fieldErrorSupportingText(lineErrors?.from),
                    )
                    WorkOrderDateTimePickerField(
                        iso = row.toIso,
                        label = "To",
                        placeholder = "End date & time",
                        flightOffset = flightOffset,
                        defaultInitialIso = row.fromIso.takeIf { it.isNotBlank() } ?: scheduleAnchorIso,
                        onIsoConfirmed = { onChange(row.copy(toIso = it)) },
                        modifier = Modifier.weight(1f),
                        isError = lineErrors?.to != null,
                        supportingText = fieldErrorSupportingText(lineErrors?.to),
                    )
                }
                OutlinedTextField(
                    value = row.description,
                    onValueChange = { onChange(row.copy(description = it)) },
                    modifier = Modifier.fillMaxWidth(),
                    shape = fieldShape,
                    label = { Text("Notes (optional)") },
                    placeholder = { Text("Optional detail for this service") },
                    minLines = 2,
                    maxLines = 4,
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskLineCard(
    lineNumber: Int,
    flightOffset: ZoneOffset,
    scheduleAnchorIso: String,
    row: TaskFormRow,
    lineErrors: TaskLineSubmitFieldErrors?,
    employees: List<EmployeeEntity>,
    tools: List<ToolEntity>,
    materials: List<MaterialEntity>,
    generalSupports: List<GeneralSupportEntity>,
    onChange: (TaskFormRow) -> Unit,
    onRemove: () -> Unit,
    canRemove: Boolean,
) {
    val performerHighlight = row.employeeIds.isNotEmpty()
    val chipLabel = remember(row.taskType, row.description, row.employeeIds) {
        taskRecapChipText(row)
    }

    val fieldShape = RoundedCornerShape(14.dp)
    val radius = 14.dp

    OutlinedCard(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(radius),
        colors = CardDefaults.outlinedCardColors(),
    ) {
        Row(
            Modifier
                .fillMaxWidth()
                .height(IntrinsicSize.Min),
        ) {
            Spacer(
                modifier = Modifier
                    .fillMaxHeight()
                    .width(5.dp)
                    .background(
                        color = MaterialTheme.colorScheme.primary,
                        shape = RoundedCornerShape(
                            topStart = radius,
                            bottomStart = radius,
                        ),
                    ),
            )
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(start = 12.dp, end = 14.dp, top = 14.dp, bottom = 14.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Row(
                    Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    WorkOrderLineRecapChip(
                        lineNumber = lineNumber,
                        chipLabel = chipLabel,
                        emphasize = performerHighlight,
                        showReturnToRamp = row.returnToRamp,
                        modifier = Modifier.weight(1f),
                    )
                    if (canRemove) {
                        IconButton(onClick = onRemove) {
                            Icon(
                                Icons.Default.DeleteOutline,
                                contentDescription = "Remove task line",
                                tint = MaterialTheme.colorScheme.error,
                            )
                        }
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    FilterChip(
                        selected = row.taskType == TaskTypeKind.Major,
                        onClick = { onChange(row.copy(taskType = TaskTypeKind.Major)) },
                        label = { Text("Major") },
                        modifier = Modifier.weight(1f),
                        colors = FilterChipDefaults.filterChipColors(
                            selectedContainerColor = MaterialTheme.colorScheme.errorContainer,
                            selectedLabelColor = MaterialTheme.colorScheme.onErrorContainer,
                        ),
                    )
                    FilterChip(
                        selected = row.taskType == TaskTypeKind.Minor,
                        onClick = { onChange(row.copy(taskType = TaskTypeKind.Minor)) },
                        label = { Text("Minor") },
                        modifier = Modifier.weight(1f),
                    )
                }

                val employeeOrderedIds = remember(employees) { employees.map { it.staffMemberId } }
                MultiSelectDropdownField(
                    label = "Performed by",
                    selectedSummary = formatMultiSelectSummary(
                        row.employeeIds.mapNotNull { id ->
                            employees.find { it.staffMemberId == id }?.workOrderPickerDisplayLine()
                        },
                    ),
                    placeholder = "Tap to choose one or more",
                    options = employees,
                    selectedKeys = row.employeeIds.toSet(),
                    optionKey = { it.staffMemberId },
                    renderOption = { it.workOrderPickerDisplayLine() },
                    secondaryLine = { emp -> emp.employeeNumber.takeIf { it.isNotBlank() } },
                    readOnly = employees.isEmpty(),
                    isError = lineErrors?.performers != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.performers),
                    onSelectionChange = { keys ->
                        onChange(row.copy(employeeIds = idsPreservingCatalogOrder(keys, employeeOrderedIds)))
                    },
                )
                if (employees.isEmpty()) {
                    Text(
                        text = "No station employees in cache. Pull to refresh or open Sync Center.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                val toolOrderedIds = remember(tools) { tools.map { it.toolId } }
                MultiSelectDropdownField(
                    label = "Tools",
                    selectedSummary = formatMultiSelectSummary(
                        row.toolIds.mapNotNull { id -> tools.find { it.toolId == id }?.name },
                    ),
                    placeholder = "Optional — tap to add tools",
                    options = tools,
                    selectedKeys = row.toolIds.toSet(),
                    optionKey = { it.toolId },
                    renderOption = { it.name },
                    readOnly = tools.isEmpty(),
                    onSelectionChange = { keys ->
                        onChange(row.copy(toolIds = idsPreservingCatalogOrder(keys, toolOrderedIds)))
                    },
                )
                val materialOrderedIds = remember(materials) { materials.map { it.materialId } }
                MultiSelectDropdownField(
                    label = "Materials",
                    selectedSummary = formatMultiSelectSummary(
                        row.materialIds.mapNotNull { id -> materials.find { it.materialId == id }?.name },
                    ),
                    placeholder = "Optional — tap to add materials",
                    options = materials,
                    selectedKeys = row.materialIds.toSet(),
                    optionKey = { it.materialId },
                    renderOption = { it.name },
                    readOnly = materials.isEmpty(),
                    onSelectionChange = { keys ->
                        onChange(row.copy(materialIds = idsPreservingCatalogOrder(keys, materialOrderedIds)))
                    },
                )
                val gsOrderedIds = remember(generalSupports) { generalSupports.map { it.generalSupportId } }
                MultiSelectDropdownField(
                    label = "General supports",
                    selectedSummary = formatMultiSelectSummary(
                        row.generalSupportIds.mapNotNull { id ->
                            generalSupports.find { it.generalSupportId == id }?.name
                        },
                    ),
                    placeholder = "Optional — tap to add general supports",
                    options = generalSupports,
                    selectedKeys = row.generalSupportIds.toSet(),
                    optionKey = { it.generalSupportId },
                    renderOption = { it.name },
                    readOnly = generalSupports.isEmpty(),
                    onSelectionChange = { keys ->
                        onChange(
                            row.copy(
                                generalSupportIds = idsPreservingCatalogOrder(keys, gsOrderedIds),
                            ),
                        )
                    },
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    WorkOrderDateTimePickerField(
                        iso = row.fromIso,
                        label = "From",
                        placeholder = "Start date & time",
                        flightOffset = flightOffset,
                        defaultInitialIso = scheduleAnchorIso,
                        onIsoConfirmed = { onChange(row.copy(fromIso = it)) },
                        modifier = Modifier.weight(1f),
                        isError = lineErrors?.from != null,
                        supportingText = fieldErrorSupportingText(lineErrors?.from),
                    )
                    WorkOrderDateTimePickerField(
                        iso = row.toIso,
                        label = "To",
                        placeholder = "End date & time",
                        flightOffset = flightOffset,
                        defaultInitialIso = row.fromIso.takeIf { it.isNotBlank() } ?: scheduleAnchorIso,
                        onIsoConfirmed = { onChange(row.copy(toIso = it)) },
                        modifier = Modifier.weight(1f),
                        isError = lineErrors?.to != null,
                        supportingText = fieldErrorSupportingText(lineErrors?.to),
                    )
                }

                Text(
                    text = "Attachments",
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    PhotoAttachmentButton(
                        modifier = Modifier.weight(1f),
                        onAttachment = { onChange(row.copy(attachments = row.attachments + it)) },
                    )
                    VoiceAttachmentButton(
                        modifier = Modifier.weight(1f),
                        onAttachment = { onChange(row.copy(attachments = row.attachments + it)) },
                    )
                    DocumentAttachmentButton(
                        modifier = Modifier.weight(1f),
                        onAttachment = { onChange(row.copy(attachments = row.attachments + it)) },
                    )
                }
                if (row.attachments.isNotEmpty()) {
                    Column(
                        verticalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        row.attachments.forEachIndexed { idx, att ->
                            TaskAttachmentRow(
                                attachment = att,
                                onRemove = {
                                    val next = row.attachments.toMutableList().apply { removeAt(idx) }
                                    onChange(row.copy(attachments = next))
                                },
                            )
                        }
                    }
                }

                OutlinedTextField(
                    value = row.description,
                    onValueChange = { onChange(row.copy(description = it)) },
                    modifier = Modifier.fillMaxWidth(),
                    shape = fieldShape,
                    label = { Text("Description (optional)") },
                    placeholder = { Text("What was observed or corrected") },
                    minLines = 2,
                    maxLines = 4,
                )
            }
        }
    }
}
