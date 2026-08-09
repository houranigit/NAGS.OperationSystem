package com.nags.operations.ui.workorder

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.allowedPerformedServiceIds
import com.nags.operations.ui.components.WorkOrderDateTimePickerField
import java.time.ZoneId

/** Full nested editor shared by the work-order wizard and standalone flight action. */
@Composable
fun ReturnToRampOccurrenceCard(
    occurrenceNumber: Int,
    row: ReturnToRampFormRow,
    errors: ReturnToRampSubmitFieldErrors?,
    flightOffset: ZoneId,
    scheduleAnchorIso: String,
    services: List<ServiceEntity>,
    employees: List<EmployeeEntity>,
    tools: List<ToolEntity>,
    materials: List<MaterialEntity>,
    generalSupports: List<GeneralSupportEntity>,
    onChange: (ReturnToRampFormRow) -> Unit,
    onRemove: () -> Unit,
    canRemove: Boolean,
    onAddService: () -> Unit,
    onServiceChange: (ServiceLineFormRow) -> Unit,
    onServiceRemove: (Long) -> Unit,
    onServiceAttachmentAdded: (Long, TaskAttachmentDraft) -> Unit,
    onServiceAttachmentRemoved: (Long, TaskAttachmentDraft) -> Unit,
    onAddTask: () -> Unit,
    onTaskChange: (TaskFormRow) -> Unit,
    onTaskRemove: (Long) -> Unit,
    onTaskAttachmentAdded: (Long, TaskAttachmentDraft) -> Unit,
    onTaskAttachmentRemoved: (Long, TaskAttachmentDraft) -> Unit,
    modifier: Modifier = Modifier,
) {
    OutlinedCard(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    "Return to ramp $occurrenceNumber",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                if (canRemove) {
                    IconButton(onClick = onRemove) {
                        Icon(Icons.Default.DeleteOutline, contentDescription = "Remove return to ramp")
                    }
                }
            }

            WorkOrderDateTimePickerField(
                iso = row.fromIso,
                label = "From",
                placeholder = "Set return-to-ramp start",
                flightOffset = flightOffset,
                defaultInitialIso = scheduleAnchorIso,
                onIsoConfirmed = { onChange(row.copy(fromIso = it)) },
                isError = errors?.from != null,
                supportingText = fieldErrorSupportingText(errors?.from),
            )
            WorkOrderDateTimePickerField(
                iso = row.toIso,
                label = "To",
                placeholder = "Set return-to-ramp end",
                flightOffset = flightOffset,
                defaultInitialIso = row.fromIso.ifBlank { scheduleAnchorIso },
                onIsoConfirmed = { onChange(row.copy(toIso = it)) },
                isError = errors?.to != null,
                supportingText = fieldErrorSupportingText(errors?.to),
            )
            OutlinedTextField(
                value = row.description,
                onValueChange = {
                    onChange(row.copy(description = it.take(WorkOrderFormLimits.LineDescription)))
                },
                modifier = Modifier.fillMaxWidth(),
                label = { Text("Description (optional)") },
                minLines = 3,
                maxLines = 6,
                shape = RoundedCornerShape(14.dp),
                isError = errors?.description != null,
                supportingText = fieldErrorSupportingText(errors?.description),
            )
            errors?.activity?.let { message ->
                Text(message, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }

            ServiceLinesSectionHeading(
                performedServicesUnavailable = services.allowedPerformedServiceIds().isEmpty(),
                catalogsMissingEmployees = employees.isEmpty(),
            )
            row.serviceLines.forEachIndexed { index, line ->
                ServiceLineCard(
                    lineNumber = index + 1,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = row.fromIso.ifBlank { scheduleAnchorIso },
                    row = line,
                    lineErrors = errors?.serviceLinesByKey?.get(line.localKey),
                    services = services,
                    employees = employees,
                    onChange = onServiceChange,
                    onAttachmentAdded = { onServiceAttachmentAdded(line.localKey, it) },
                    onAttachmentRemoved = { onServiceAttachmentRemoved(line.localKey, it) },
                    onRemove = { onServiceRemove(line.localKey) },
                    canRemove = true,
                )
            }
            Button(
                onClick = onAddService,
                enabled = services.allowedPerformedServiceIds().isNotEmpty(),
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(14.dp),
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
            ) {
                Icon(Icons.Default.Add, contentDescription = null)
                Spacer(Modifier.width(10.dp))
                Text("Add service")
            }

            FormSectionTitle("Tasks")
            TasksSectionHeading(
                catalogsMissingEmployees = employees.isEmpty(),
                catalogsMissingTools = tools.isEmpty(),
                catalogsMissingMaterials = materials.isEmpty(),
                catalogsMissingGeneralSupports = generalSupports.isEmpty(),
            )
            row.tasks.forEachIndexed { index, task ->
                TaskLineCard(
                    lineNumber = index + 1,
                    flightOffset = flightOffset,
                    scheduleAnchorIso = row.fromIso.ifBlank { scheduleAnchorIso },
                    row = task,
                    lineErrors = errors?.tasksByKey?.get(task.localKey),
                    employees = employees,
                    tools = tools,
                    materials = materials,
                    generalSupports = generalSupports,
                    onChange = onTaskChange,
                    onAttachmentAdded = { onTaskAttachmentAdded(task.localKey, it) },
                    onAttachmentRemoved = { onTaskAttachmentRemoved(task.localKey, it) },
                    onRemove = { onTaskRemove(task.localKey) },
                    canRemove = true,
                )
            }
            Button(
                onClick = onAddTask,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(14.dp),
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
            ) {
                Icon(Icons.Default.Add, contentDescription = null)
                Spacer(Modifier.width(10.dp))
                Text("Add task")
            }
        }
    }
}
