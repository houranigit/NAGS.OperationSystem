package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.R

/** Repeatable task entries remain independent from scheduled services and from one another. */
@Composable
internal fun FlightTaskSection(
    items: List<FlightTaskDraft>,
    catalogs: FlightOperationsCatalogs,
    referenceYear: Int,
    onChange: ((List<FlightTaskDraft>) -> List<FlightTaskDraft>) -> Unit,
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

    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(stringResource(R.string.work_tasks), Modifier.padding(horizontal = 14.dp).semantics { heading() },
            style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.primary)
        FlightFormFieldGroup {
            items.forEachIndexed { index, task ->
                key(task.id) {
                    FlightWorkRemovableEntry(
                        id = task.id,
                        removeDescription = stringResource(R.string.work_remove_item, "$taskLabel ${index + 1}"),
                        onRemove = { onChange { current -> current.filterNot { it.id == task.id } } },
                    ) {
                        FlightWorkChoiceRow(
                            label = stringResource(R.string.work_task_type),
                            value = when (task.taskType) {
                                "Major" -> major
                                "Minor" -> minor
                                else -> stringResource(R.string.work_select_task_type)
                            },
                            description = stringResource(R.string.work_task_type),
                            isPlaceholder = task.taskType.isBlank(),
                            onClick = { openPicker(task.id, "type") },
                        )
                        FlightFormDivider()
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                FlightWorkChoiceRow(
                                    label = stringResource(R.string.work_ata_chapter),
                                    value = if (task.ataChapterId.isBlank()) stringResource(R.string.work_select_ata)
                                        else "${task.ataChapterCode} · ${task.ataChapterTitle}",
                                    description = stringResource(R.string.work_ata_chapter),
                                    isPlaceholder = task.ataChapterId.isBlank(),
                                    onClick = { openPicker(task.id, "ata") },
                                )
                            }
                            if (task.ataChapterId.isNotBlank()) IconButton(
                                onClick = { updateTask(task.id) { it.copy(ataChapterId = "", ataChapterCode = "", ataChapterTitle = "") } },
                                modifier = Modifier.size(48.dp),
                            ) {
                                Icon(Icons.Outlined.Close, stringResource(R.string.work_clear_ata), Modifier.size(18.dp),
                                    tint = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                        FlightFormDivider()
                        FlightWorkPeriodFields(task.period, referenceYear) { period -> updateTask(task.id) { it.copy(period = period) } }
                        FlightFormDivider()
                        FlightWorkReferenceRow(task.reference,
                            onChange = { value -> updateTask(task.id) { it.copy(reference = value) } })
                    }
                    FlightFormDivider()
                    FlightWorkEmployeeList(task.employees, catalogs.stationStaff, showRemarks = true, referenceYear = referenceYear,
                        onChange = { employees -> updateTask(task.id) { it.copy(employees = employees) } })
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.Tool, task.tools, catalogs.tools, referenceYear,
                        onChange = { tools -> updateTask(task.id) { it.copy(tools = tools) } })
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.Material, task.materials, catalogs.materials, referenceYear,
                        onChange = { materials -> updateTask(task.id) { it.copy(materials = materials) } })
                    FlightFormDivider()
                    FlightWorkResourceList(FlightWorkResourceKind.GeneralSupport, task.generalSupports, catalogs.generalSupports, referenceYear,
                        onChange = { supports -> updateTask(task.id) { it.copy(generalSupports = supports) } })
                    FlightFormDivider()
                    FlightRemarksComposer(task.remarks,
                        onRemarksChange = { value -> updateTask(task.id) { it.copy(remarks = value) } },
                        attachments = task.attachments,
                        onAttachmentsChange = { attachments -> updateTask(task.id) { it.copy(attachments = attachments) } },
                        description = stringResource(R.string.work_task_remarks))
                    FlightFormDivider()
                }
            }
            FlightWorkAddRow(stringResource(R.string.work_add_task)) {
                val added = FlightTaskDraft()
                onChange { current -> current + added }
            }
        }
    }

    val activeTask = items.firstOrNull { it.id == pickerTaskId }
    if (activeTask != null && pickerKind == "type") {
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
    if (activeTask != null && pickerKind == "ata") {
        val choices = catalogs.ataChapters.map { FlightWorkChoice(it.id, "${it.code} · ${it.title}", it.categoryName) }.toMutableList()
        if (activeTask.ataChapterId.isNotBlank() && choices.none { it.id == activeTask.ataChapterId }) {
            choices += FlightWorkChoice(activeTask.ataChapterId, "${activeTask.ataChapterCode} · ${activeTask.ataChapterTitle}")
        }
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
