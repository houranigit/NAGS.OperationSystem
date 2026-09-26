package com.nags.operations.ui.workorder

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
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
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.data.WellKnownMasterDataIds
import com.nags.operations.data.TaskTypeKind
import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.db.entities.AtaChapterEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.isAllowedPerformedOption
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.workOrderPickerDisplayLine
import com.nags.operations.ui.components.AttachmentActionsRow
import com.nags.operations.ui.components.WorkOrderDateTimeRange
import com.nags.operations.ui.components.InlineSearchableDropdownField
import com.nags.operations.ui.components.MultiSelectDropdownField
import com.nags.operations.ui.components.TaskAttachmentRow
import com.nags.operations.ui.components.WorkOrderServicePicker
import com.nags.operations.ui.components.formatMultiSelectSummary
import java.time.ZoneId

private fun resolvedServiceName(row: ServiceLineFormRow, services: List<ServiceEntity>): String? =
    services.firstOrNull { it.serviceId == row.serviceId }?.name ?: row.serviceName

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
    performedServicesUnavailable: Boolean,
    catalogsMissingEmployees: Boolean,
) {
    Column(Modifier.fillMaxWidth()) {
        Text(
            text = "Services",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(top = 8.dp),
        )
        if (performedServicesUnavailable || catalogsMissingEmployees) {
            val hint = when {
                performedServicesUnavailable && catalogsMissingEmployees ->
                    "No allowed performed services or employees are available — sync from the flight list or Sync Center."
                performedServicesUnavailable ->
                    "No performed services are allowed for your manpower type. Sync if your access was recently changed."
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
    flightOffset: ZoneId,
    scheduleAnchorIso: String,
    row: ServiceLineFormRow,
    lineErrors: ServiceLineSubmitFieldErrors?,
    services: List<ServiceEntity>,
    employees: List<EmployeeEntity>,
    onChange: (ServiceLineFormRow) -> Unit,
    onAttachmentAdded: (TaskAttachmentDraft) -> Unit,
    onAttachmentRemoved: (TaskAttachmentDraft) -> Unit,
    onRemove: () -> Unit,
    canRemove: Boolean,
) {
    val hasServiceSelected = resolvedServiceName(row, services) != null
    val selectedService = services.firstOrNull { it.serviceId == row.serviceId }
    val selectedServiceIsAllowed = row.serviceId == null ||
        selectedService?.isAllowedPerformedOption() == true
    val chipLabel = remember(row.serviceId, row.serviceName, services) {
        serviceRecapChipText(row, services)
    }

    val fieldShape = RoundedCornerShape(14.dp)
    val radius = 14.dp

    OutlinedCard(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(radius),
        colors = CardDefaults.outlinedCardColors(),
    ) {
        Box(Modifier.fillMaxWidth()) {
            // The rail follows the content height without constraining wrapping form rows.
            Box(Modifier.matchParentSize()) {
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
            }
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(start = 17.dp, end = 14.dp, top = 14.dp, bottom = 14.dp),
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
                    selectedNameFallback = row.serviceName,
                    options = services,
                    onSelected = { svc ->
                        onChange(row.copy(serviceId = svc.serviceId, serviceName = svc.name))
                    },
                    onCleared = { onChange(row.copy(serviceId = null, serviceName = null)) },
                    isError = lineErrors?.serviceType != null || !selectedServiceIsAllowed,
                    supportingText = fieldErrorSupportingText(lineErrors?.serviceType),
                )
                if (!selectedServiceIsAllowed) {
                    Text(
                        "This service is no longer allowed for your manpower type. Remove or replace it before submitting.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }
                WorkOrderDateTimeRange(
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    flightOffset = flightOffset,
                    defaultInitialIso = scheduleAnchorIso,
                    onFromConfirmed = { onChange(row.copy(fromIso = it)) },
                    onToConfirmed = { onChange(row.copy(toIso = it)) },
                    fromIsError = lineErrors?.from != null,
                    toIsError = lineErrors?.to != null,
                    fromSupportingText = fieldErrorSupportingText(lineErrors?.from),
                    toSupportingText = fieldErrorSupportingText(lineErrors?.to),
                )

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
                    isError = lineErrors?.performer != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.performer),
                    onSelectionChange = { keys ->
                        val ids = idsPreservingCatalogOrder(keys, employeeOrderedIds)
                        onChange(row.copy(
                            employeeIds = ids,
                            employeePeriods = employeePeriodsForSelection(
                                ids, row.employeePeriods, row.fromIso, row.toIso, row.employeeIds,
                            ),
                        ))
                    },
                )
                if (employees.isEmpty()) {
                    Text(
                        text = "No station employees in cache. Pull to refresh or open Sync Center.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                EmployeePeriodFields(
                    employeeIds = row.employeeIds,
                    periods = row.employeePeriods,
                    employees = employees,
                    lineFromIso = row.fromIso,
                    lineToIso = row.toIso,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = scheduleAnchorIso,
                    onPeriodChanged = { id, period -> onChange(row.copy(employeePeriods = row.employeePeriods + (id to period))) },
                )

                Text(
                    text = "Attachments (${row.existingAttachmentNames.size + row.attachments.size}/${WorkOrderFormLimits.ServiceAttachments})",
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                )
                if (row.existingAttachmentNames.isNotEmpty()) {
                    Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                        Text(
                            "Already uploaded",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        row.existingAttachmentNames.forEach { name ->
                            Text(
                                "• $name",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
                val attachmentCount = row.existingAttachmentNames.size + row.attachments.size
                if (attachmentCount < WorkOrderFormLimits.ServiceAttachments) {
                    AttachmentActionsRow(onAttachment = onAttachmentAdded)
                } else {
                    Text(
                        "Attachment limit reached. Remove a new attachment before adding another.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (row.attachments.isNotEmpty()) {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        row.attachments.forEach { attachment ->
                            TaskAttachmentRow(
                                attachment = attachment,
                                onRemove = { onAttachmentRemoved(attachment) },
                            )
                        }
                    }
                }
                lineErrors?.attachments?.let { message ->
                    Text(
                        message,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                OutlinedTextField(
                    value = row.description,
                    onValueChange = {
                        onChange(row.copy(description = it.take(WorkOrderFormLimits.LineDescription)))
                    },
                    modifier = Modifier.fillMaxWidth(),
                    shape = fieldShape,
                    label = { Text(if (row.serviceId.equals(WellKnownMasterDataIds.UnknownService, true)) "Notes (required)" else "Notes (optional)") },
                    placeholder = { Text("Describe the service performed") },
                    minLines = 2,
                    maxLines = 4,
                    isError = lineErrors?.description != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.description),
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskLineCard(
    lineNumber: Int,
    flightOffset: ZoneId,
    scheduleAnchorIso: String,
    row: TaskFormRow,
    lineErrors: TaskLineSubmitFieldErrors?,
    employees: List<EmployeeEntity>,
    tools: List<ToolEntity>,
    materials: List<MaterialEntity>,
    generalSupports: List<GeneralSupportEntity>,
    ataChapters: List<AtaChapterEntity>,
    onChange: (TaskFormRow) -> Unit,
    onAttachmentAdded: (TaskAttachmentDraft) -> Unit,
    onAttachmentRemoved: (TaskAttachmentDraft) -> Unit,
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
        Box(Modifier.fillMaxWidth()) {
            // The rail follows the content height without constraining wrapping form rows.
            Box(Modifier.matchParentSize()) {
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
            }
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(start = 17.dp, end = 14.dp, top = 14.dp, bottom = 14.dp),
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
                lineErrors?.taskType?.let { message ->
                    Text(
                        text = message,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                InlineSearchableDropdownField(
                    label = "ATA Chapters (optional)",
                    selectedText = row.ataChapterDisplayLabel(ataChapters),
                    placeholder = "Search chapter number or title",
                    // The cache contains only active items in active categories. Never add a
                    // saved inactive chapter back to the options just to render its label.
                    options = ataChapters,
                    renderOption = { it.displayLabel },
                    secondaryLine = { it.categoryName },
                    onSelect = { chapter ->
                        onChange(row.copy(
                            ataChapterId = chapter.id,
                            ataChapterCode = chapter.code,
                            ataChapterTitle = chapter.title,
                        ))
                    },
                    onClearSelection = {
                        onChange(row.copy(ataChapterId = null, ataChapterCode = null, ataChapterTitle = null))
                    },
                    hasSelection = row.ataChapterId != null,
                    supportingText = fieldErrorSupportingText(when {
                        row.ataChapterId != null && ataChapters.none { it.id == row.ataChapterId } ->
                            "Saved chapter is currently unavailable for new selections."
                        ataChapters.isEmpty() ->
                            "No active ATA chapters available. Sync master data to refresh."
                        else -> null
                    }),
                )

                OutlinedTextField(
                    value = row.description,
                    onValueChange = {
                        onChange(row.copy(description = it.take(WorkOrderFormLimits.LineDescription)))
                    },
                    modifier = Modifier.fillMaxWidth(),
                    shape = fieldShape,
                    label = { Text("Description (optional)") },
                    placeholder = { Text("What was observed or corrected") },
                    minLines = 2,
                    maxLines = 4,
                    isError = lineErrors?.description != null,
                    supportingText = fieldErrorSupportingText(lineErrors?.description),
                )

                WorkOrderDateTimeRange(
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    flightOffset = flightOffset,
                    defaultInitialIso = scheduleAnchorIso,
                    onFromConfirmed = { onChange(row.copy(fromIso = it)) },
                    onToConfirmed = { onChange(row.copy(toIso = it)) },
                    fromIsError = lineErrors?.from != null,
                    toIsError = lineErrors?.to != null,
                    fromSupportingText = fieldErrorSupportingText(lineErrors?.from),
                    toSupportingText = fieldErrorSupportingText(lineErrors?.to),
                )

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
                        val ids = idsPreservingCatalogOrder(keys, employeeOrderedIds)
                        onChange(row.copy(
                            employeeIds = ids,
                            employeePeriods = employeePeriodsForSelection(
                                ids, row.employeePeriods, row.fromIso, row.toIso, row.employeeIds,
                            ),
                        ))
                    },
                )
                EmployeePeriodFields(
                    employeeIds = row.employeeIds,
                    periods = row.employeePeriods,
                    employees = employees,
                    lineFromIso = row.fromIso,
                    lineToIso = row.toIso,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = scheduleAnchorIso,
                    onPeriodChanged = { id, period -> onChange(row.copy(employeePeriods = row.employeePeriods + (id to period))) },
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
                        val ids = idsPreservingCatalogOrder(keys, toolOrderedIds)
                        onChange(
                            row.copy(
                                toolIds = ids,
                                toolQuantities = quantitiesForSelection(ids, row.toolQuantities),
                                toolUsages = usagesForSelection(
                                    ids,
                                    row.toolUsages,
                                    row.toolQuantities,
                                    tools.associate { it.toolId to it.calculationType },
                                    ResourceCalculationType.Duration,
                                    row.fromIso,
                                    row.toIso,
                                ),
                            ),
                        )
                    },
                )
                ResourceUsageFields(
                    selectedIds = row.toolIds,
                    namesById = tools.associate { it.toolId to it.name },
                    calculationTypesById = tools.associate { it.toolId to it.calculationType },
                    defaultCalculationType = ResourceCalculationType.Duration,
                    usages = row.toolUsages,
                    legacyQuantities = row.toolQuantities,
                    taskFromIso = row.fromIso,
                    taskToIso = row.toIso,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = scheduleAnchorIso,
                    error = lineErrors?.tools,
                    onUsageChanged = { id, usage ->
                        onChange(row.copy(
                            toolUsages = row.toolUsages + (id to usage),
                            toolQuantities = usage.quantity?.let { row.toolQuantities + (id to it) }
                                ?: row.toolQuantities,
                        ))
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
                        val ids = idsPreservingCatalogOrder(keys, materialOrderedIds)
                        onChange(
                            row.copy(
                                materialIds = ids,
                                materialQuantities = quantitiesForSelection(ids, row.materialQuantities),
                                materialUsages = usagesForSelection(
                                    ids,
                                    row.materialUsages,
                                    row.materialQuantities,
                                    materials.associate { it.materialId to it.calculationType },
                                    ResourceCalculationType.Quantity,
                                    row.fromIso,
                                    row.toIso,
                                ),
                            ),
                        )
                    },
                )
                ResourceUsageFields(
                    selectedIds = row.materialIds,
                    namesById = materials.associate { it.materialId to it.name },
                    calculationTypesById = materials.associate { it.materialId to it.calculationType },
                    defaultCalculationType = ResourceCalculationType.Quantity,
                    usages = row.materialUsages,
                    legacyQuantities = row.materialQuantities,
                    taskFromIso = row.fromIso,
                    taskToIso = row.toIso,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = scheduleAnchorIso,
                    error = lineErrors?.materials,
                    onUsageChanged = { id, usage ->
                        onChange(row.copy(
                            materialUsages = row.materialUsages + (id to usage),
                            materialQuantities = usage.quantity?.let { row.materialQuantities + (id to it) }
                                ?: row.materialQuantities,
                        ))
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
                        val ids = idsPreservingCatalogOrder(keys, gsOrderedIds)
                        onChange(
                            row.copy(
                                generalSupportIds = ids,
                                generalSupportQuantities = quantitiesForSelection(
                                    ids,
                                    row.generalSupportQuantities,
                                ),
                                generalSupportUsages = usagesForSelection(
                                    ids,
                                    row.generalSupportUsages,
                                    row.generalSupportQuantities,
                                    generalSupports.associate { it.generalSupportId to it.calculationType },
                                    ResourceCalculationType.Quantity,
                                    row.fromIso,
                                    row.toIso,
                                ),
                            ),
                        )
                    },
                )
                ResourceUsageFields(
                    selectedIds = row.generalSupportIds,
                    namesById = generalSupports.associate { it.generalSupportId to it.name },
                    calculationTypesById = generalSupports.associate { it.generalSupportId to it.calculationType },
                    defaultCalculationType = ResourceCalculationType.Quantity,
                    usages = row.generalSupportUsages,
                    legacyQuantities = row.generalSupportQuantities,
                    taskFromIso = row.fromIso,
                    taskToIso = row.toIso,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = scheduleAnchorIso,
                    error = lineErrors?.generalSupports,
                    onUsageChanged = { id, usage ->
                        onChange(
                            row.copy(
                                generalSupportUsages = row.generalSupportUsages + (id to usage),
                                generalSupportQuantities = usage.quantity?.let {
                                    row.generalSupportQuantities + (id to it)
                                } ?: row.generalSupportQuantities,
                            ),
                        )
                    },
                )

                Text(
                    text = "Attachments (${row.existingAttachmentNames.size + row.attachments.size}/${WorkOrderFormLimits.TaskAttachments})",
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                )
                if (row.existingAttachmentNames.isNotEmpty()) {
                    Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                        Text(
                            "Already uploaded",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        row.existingAttachmentNames.forEach { name ->
                            Text(
                                "• $name",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
                val attachmentCount = row.existingAttachmentNames.size + row.attachments.size
                if (attachmentCount < WorkOrderFormLimits.TaskAttachments) {
                    AttachmentActionsRow(onAttachment = onAttachmentAdded)
                } else {
                    Text(
                        "Attachment limit reached. Remove a new attachment before adding another.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (row.attachments.isNotEmpty()) {
                    Column(
                        verticalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        row.attachments.forEach { att ->
                            TaskAttachmentRow(
                                attachment = att,
                                onRemove = { onAttachmentRemoved(att) },
                            )
                        }
                    }
                }
                lineErrors?.attachments?.let { message ->
                    Text(
                        message,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

            }
        }
    }
}

@Composable
private fun ResourceUsageFields(
    selectedIds: List<String>,
    namesById: Map<String, String>,
    calculationTypesById: Map<String, ResourceCalculationType>,
    defaultCalculationType: ResourceCalculationType,
    usages: Map<String, ResourceUsageForm>,
    legacyQuantities: Map<String, Double>,
    taskFromIso: String,
    taskToIso: String,
    flightOffset: ZoneId,
    scheduleAnchorIso: String,
    error: String?,
    onUsageChanged: (String, ResourceUsageForm) -> Unit,
) {
    if (selectedIds.isEmpty()) return
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        selectedIds.forEach { id ->
            val calculationType = usages[id]?.calculationType
                ?: calculationTypesById[id]
                ?: defaultCalculationType
            val usage = resourceUsage(
                id,
                usages,
                legacyQuantities,
                calculationType,
                taskFromIso,
                taskToIso,
            )
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "${namesById[id] ?: "Selected item"} · ${calculationType.name}",
                    style = MaterialTheme.typography.bodyMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (WellKnownMasterDataIds.isUnknownResource(id) || usage.description.isNotBlank()) {
                    OutlinedTextField(
                        value = usage.description,
                        onValueChange = { onUsageChanged(id, usage.copy(description = it.take(WorkOrderFormLimits.LineDescription))) },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text(if (WellKnownMasterDataIds.isUnknownResource(id)) "Notes (required)" else "Notes") },
                        placeholder = { Text("Describe this item") },
                        minLines = 2,
                        isError = error != null && WellKnownMasterDataIds.isUnknownResource(id) && usage.description.isBlank(),
                    )
                }
                if (calculationType == ResourceCalculationType.Quantity) {
                    val quantity = usage.quantity ?: resourceQuantity(legacyQuantities, id)
                    var quantityText by remember(id, quantity) { mutableStateOf(formatQuantity(quantity)) }
                    OutlinedTextField(
                        value = quantityText,
                        onValueChange = { raw ->
                            if (raw.count { it == '.' } <= 1 && raw.all { it.isDigit() || it == '.' }) {
                                quantityText = raw
                                onUsageChanged(
                                    id,
                                    usage.copy(
                                        calculationType = ResourceCalculationType.Quantity,
                                        quantity = raw.toDoubleOrNull() ?: 0.0,
                                    ),
                                )
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("Quantity") },
                        singleLine = true,
                        isError = !isValidResourceQuantity(quantity),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    )
                } else {
                    WorkOrderDateTimeRange(
                        fromIso = usage.fromIso,
                        toIso = usage.toIso.orEmpty(),
                        flightOffset = flightOffset,
                        defaultInitialIso = taskFromIso.ifBlank { scheduleAnchorIso },
                        onFromConfirmed = { value ->
                            onUsageChanged(
                                id,
                                usage.copy(
                                    calculationType = ResourceCalculationType.Duration,
                                    quantity = null,
                                    fromIso = value,
                                ),
                            )
                        },
                        onToConfirmed = { value ->
                            onUsageChanged(
                                id,
                                usage.copy(
                                    calculationType = ResourceCalculationType.Duration,
                                    quantity = null,
                                    toIso = value,
                                ),
                            )
                        },
                        fromIsError = usage.fromIso.isBlank(),
                        optionalTo = true,
                    )
                    if (usage.toIso.isNullOrBlank()) {
                        Text(
                            "To is optional; leave it empty while this item is in use.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    } else {
                        TextButton(
                            onClick = {
                                onUsageChanged(
                                    id,
                                    usage.copy(
                                        calculationType = ResourceCalculationType.Duration,
                                        quantity = null,
                                        toIso = null,
                                    ),
                                )
                            },
                            modifier = Modifier.align(Alignment.End),
                        ) { Text("Clear To") }
                    }
                }
            }
        }
        error?.let { message ->
            Text(
                text = message,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
            )
        }
    }
}

private fun formatQuantity(value: Double): String =
    if (value.isFinite() && value % 1.0 == 0.0) value.toLong().toString() else value.toString()

@Composable
private fun EmployeePeriodFields(
    employeeIds: List<String>,
    periods: Map<String, EmployeePeriodForm>,
    employees: List<EmployeeEntity>,
    lineFromIso: String,
    lineToIso: String,
    flightOffset: ZoneId,
    scheduleAnchorIso: String,
    onPeriodChanged: (String, EmployeePeriodForm) -> Unit,
) {
    if (employeeIds.isEmpty()) return
    Text("Employee work periods", style = MaterialTheme.typography.labelLarge)
    Text("Set each employee's From and To within the service or task period.", style = MaterialTheme.typography.bodySmall)
    employeeIds.forEach { id ->
        val period = employeePeriod(id, periods, lineFromIso, lineToIso)
        val error = employeePeriodsError(listOf(id), mapOf(id to period), lineFromIso, lineToIso)
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(employees.firstOrNull { it.staffMemberId == id }?.workOrderPickerDisplayLine() ?: "Selected employee")
            WorkOrderDateTimeRange(
                fromIso = period.fromIso,
                toIso = period.toIso,
                flightOffset = flightOffset,
                defaultInitialIso = lineFromIso.ifBlank { scheduleAnchorIso },
                toInitialIso = lineToIso.ifBlank { period.fromIso.ifBlank { scheduleAnchorIso } },
                onFromConfirmed = { onPeriodChanged(id, period.copy(fromIso = it)) },
                onToConfirmed = { onPeriodChanged(id, period.copy(toIso = it)) },
                fromIsError = error != null,
                toIsError = error != null,
            )
            error?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error) }
        }
    }
}
