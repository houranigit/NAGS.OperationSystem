package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import java.time.ZoneId
import java.util.UUID
import com.nags.operations.R

/** Repeatable task entries remain independent from scheduled services and from one another. */
@Composable
internal fun FlightTaskSection(
    items: List<FlightTaskDraft>,
    catalogs: FlightOperationsCatalogs,
    referenceYear: Int,
    zone: ZoneId,
    onChange: ((List<FlightTaskDraft>) -> List<FlightTaskDraft>) -> Unit,
    enabled: Boolean = true,
    canEdit: (String) -> Boolean = { true },
    canSetEnd: (String) -> Boolean = { true },
    canDelete: (String) -> Boolean = { true },
    canChangeType: (String) -> Boolean = { true },
    errors: Map<String, Map<String, Int>> = emptyMap(),
    defaultEmployee: FlightEmployeeDraft? = null,
    timeBounds: FlightWorkTimeBounds = FlightWorkTimeBounds(),
) {
    var pickerTaskId by rememberSaveable { mutableStateOf<String?>(null) }
    var pickerKind by rememberSaveable { mutableStateOf<String?>(null) }
    val taskLabel = stringResource(R.string.work_task)
    val major = stringResource(R.string.work_major)
    val minor = stringResource(R.string.work_minor)
    fun updateTask(id: String, change: (FlightTaskDraft) -> FlightTaskDraft) {
        onChange { current -> current.map { if (it.id == id) change(it) else it } }
    }
    fun openPicker(id: String, kind: String) {
        pickerTaskId = id
        pickerKind = kind
    }
    fun closePicker() {
        pickerTaskId = null
        pickerKind = null
    }

    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        FlightFormSectionTitle(stringResource(R.string.work_tasks_count, items.size))
        items.forEachIndexed { index, task ->
            key(task.id) {
                var initialStartStaffIds by rememberSaveable { mutableStateOf(emptyList<String>()) }
                val fields = errors[task.id].orEmpty()
                val editable = enabled && canEdit(task.id)
                val childBounds = timeBounds.within(task.period, zone)
                FlightWorkEntryGroup {
                    FlightWorkRemovableEntry(
                        id = task.id,
                        removeDescription = stringResource(R.string.work_remove_item, "$taskLabel ${index + 1}"),
                        onRemove = { onChange { current -> current.filterNot { it.id == task.id } } },
                        compactDelete = true,
                        enabled = enabled && canDelete(task.id),
                    ) {
                        FlightWorkChoiceRow(
                            label = stringResource(R.string.work_task_number, index + 1),
                            value = when (task.taskType) {
                                "Major" -> major
                                "Minor" -> minor
                                else -> stringResource(R.string.work_select_task_type)
                            },
                            description = stringResource(R.string.work_task_type),
                            isPlaceholder = task.taskType.isBlank(),
                            leadingPadding = 0.dp,
                            enabled = editable && canChangeType(task.id),
                            errorMessage = fields["taskType"]?.let { stringResource(it) },
                            onClick = { openPicker(task.id, "type") },
                        )
                    }
                    FlightFormDivider()
                    FlightWorkReferenceRow(task.reference,
                        onChange = { value -> updateTask(task.id) { it.copy(reference = value) } },
                        enabled = editable, errorMessage = fields["customerReferenceNumber"]?.let { stringResource(it) })
                    FlightFormDivider()
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            FlightWorkChoiceRow(
                                label = stringResource(R.string.work_ata_chapter),
                                value = if (task.ataChapterId.isBlank()) stringResource(R.string.work_select_ata)
                                    else "${task.ataChapterCode} · ${task.ataChapterTitle}",
                                description = stringResource(R.string.work_ata_chapter),
                                isPlaceholder = task.ataChapterId.isBlank(),
                                enabled = editable,
                                errorMessage = fields["ataChapterId"]?.let { stringResource(it) },
                                onClick = { openPicker(task.id, "ata") },
                            )
                        }
                        if (task.ataChapterId.isNotBlank()) IconButton(
                            onClick = { updateTask(task.id) { it.copy(ataChapterId = "", ataChapterCode = "", ataChapterTitle = "") } },
                            modifier = Modifier.size(48.dp),
                            enabled = editable,
                        ) {
                            Icon(Icons.Outlined.Close, stringResource(R.string.work_clear_ata), Modifier.size(18.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    FlightFormDivider()
                    FlightWorkPeriodFields(task.period, referenceYear, zone,
                        onChange = { period -> updateTask(task.id) { taskPeriodWithInitialStaff(it, period, initialStartStaffIds.toSet()) } },
                        startEnabled = editable, endEnabled = enabled && canSetEnd(task.id),
                        startError = fields["startUtc"]?.let { stringResource(it) },
                        endError = fields["endUtc"]?.let { stringResource(it) }, seedEndNow = true, timeBounds = timeBounds,
                        onStartPickerSessionChanged = { initialBlank -> initialStartStaffIds =
                            if (initialBlank) task.employees.filter { it.period.start.isBlank() }.map { it.id } else emptyList() })
                    FlightFormDivider()
                    FlightWorkEmployeeList(task.employees, catalogs.stationStaff, showRemarks = true, referenceYear = referenceYear, zone = zone,
                        onChange = { employees -> updateTask(task.id) { it.copy(employees = employees) } }, enabled = editable,
                        errorMessage = fields["performers"]?.let { stringResource(it) }, fieldErrors = fields, timeBounds = childBounds,
                        defaultPeriod = task.period.copy(end = "", originalEndUtc = null))
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.Tool, task.tools, catalogs.tools, referenceYear, zone,
                        onChange = { tools -> updateTask(task.id) { it.copy(tools = tools) } }, enabled = editable,
                        fieldErrors = fields, parentPeriod = task.period, timeBounds = childBounds)
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.Material, task.materials, catalogs.materials, referenceYear, zone,
                        onChange = { materials -> updateTask(task.id) { it.copy(materials = materials) } }, enabled = editable,
                        fieldErrors = fields, parentPeriod = task.period, timeBounds = childBounds)
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.GeneralSupport, task.generalSupports, catalogs.generalSupports, referenceYear, zone,
                        onChange = { supports -> updateTask(task.id) { it.copy(generalSupports = supports) } }, enabled = editable,
                        fieldErrors = fields, parentPeriod = task.period, timeBounds = childBounds)
                    FlightFormDivider()
                    FlightRemarksComposer(task.remarks,
                        onRemarksChange = { value -> updateTask(task.id) { it.copy(remarks = value) } },
                        attachments = task.attachments,
                        onAttachmentsChange = { attachments -> updateTask(task.id) { it.copy(attachments = attachments) } },
                        description = stringResource(R.string.work_task_remarks), enabled = editable,
                        errorMessage = (fields["notes"] ?: fields["attachments"])?.let { stringResource(it) })
                }
            }
        }
        FlightFormFieldGroup {
            FlightWorkAddRow(stringResource(R.string.work_add_task, items.size + 1), enabled = enabled && defaultEmployee != null) {
                val period = seedBlankWorkStart(FlightWorkPeriod(), zone, timeBounds)
                val added = FlightTaskDraft(taskType = "Major", period = period,
                    employees = listOfNotNull(defaultEmployee?.copy(id = UUID.randomUUID().toString(), period = period)))
                onChange { current -> current + added }
            }
        }
    }

    val activeTask = items.firstOrNull { it.id == pickerTaskId }
    if (activeTask != null && pickerKind == "type" && enabled && canEdit(activeTask.id) && canChangeType(activeTask.id)) {
        FlightWorkPicker(
            title = stringResource(R.string.work_task_type),
            searchLabel = "",
            emptyLabel = "",
            options = listOf(FlightWorkChoice("Major", major), FlightWorkChoice("Minor", minor)),
            selectedId = activeTask.taskType,
            onSelect = { type -> updateTask(activeTask.id) { it.copy(taskType = type) } },
            onDismiss = ::closePicker,
            searchable = false,
        )
    }
    if (activeTask != null && pickerKind == "ata" && enabled && canEdit(activeTask.id)) {
        val choices = catalogs.ataChapters.map { FlightWorkChoice(it.id, "${it.code} · ${it.title}", it.categoryName) }
        FlightWorkPicker(
            title = stringResource(R.string.work_ata_chapter),
            searchLabel = stringResource(R.string.work_search_ata),
            emptyLabel = stringResource(R.string.work_no_ata),
            options = choices,
            selectedId = activeTask.ataChapterId,
            onSelect = { chapterId ->
                catalogs.ataChapters.firstOrNull { it.id == chapterId }?.let { chapter ->
                    updateTask(activeTask.id) { it.copy(ataChapterId = chapter.id, ataChapterCode = chapter.code, ataChapterTitle = chapter.title) }
                }
            },
            onDismiss = ::closePicker,
        )
    }
}
