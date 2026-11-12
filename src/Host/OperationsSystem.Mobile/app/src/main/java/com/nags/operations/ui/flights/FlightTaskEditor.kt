package com.nags.operations.ui.flights

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
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
    val taskLabel = stringResource(R.string.work_task)
    fun updateTask(id: String, change: (FlightTaskDraft) -> FlightTaskDraft) {
        onChange { current -> current.map { if (it.id == id) change(it) else it } }
    }
    fun openPicker(id: String) {
        pickerTaskId = id
    }
    fun closePicker() {
        pickerTaskId = null
    }

    Column(Modifier.animateContentSize(animationSpec = tween(200)), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        FlightFormSectionTitle(stringResource(R.string.work_tasks_count, items.size))
        items.forEachIndexed { index, task ->
            key(task.id) {
                var initialStartStaffIds by rememberSaveable { mutableStateOf(emptyList<String>()) }
                val fields = errors[task.id].orEmpty()
                val editable = enabled && canEdit(task.id)
                val childBounds = timeBounds.within(task.period, zone)
                FlightWorkEntryGroup(Modifier.flightFormNavigationTarget("task:${task.id}")) {
                    FlightWorkRemovableEntry(
                        id = task.id,
                        removeDescription = stringResource(R.string.work_remove_item, "$taskLabel ${index + 1}"),
                        onRemove = { onChange { current -> current.filterNot { it.id == task.id } } },
                        compactDelete = true,
                        enabled = enabled && canDelete(task.id),
                    ) {
                        FlightTaskTypeRow(
                            label = stringResource(R.string.work_task_number, index + 1),
                            value = task.taskType,
                            enabled = editable && canChangeType(task.id),
                            errorMessage = fields["taskType"]?.let { stringResource(it) },
                            onChange = { type -> updateTask(task.id) { it.copy(taskType = type) } },
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
                                value = if (task.ataChapterId.isBlank()) ""
                                    else "${task.ataChapterCode} · ${task.ataChapterTitle}",
                                description = stringResource(R.string.work_ata_chapter),
                                isPlaceholder = task.ataChapterId.isBlank(),
                                enabled = editable,
                                errorMessage = fields["ataChapterId"]?.let { stringResource(it) },
                                onClick = { openPicker(task.id) },
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
                        endRequired = timeBounds.upperUtc != null,
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
                        label = stringResource(R.string.work_task_remarks_heading),
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
    if (activeTask != null && enabled && canEdit(activeTask.id)) {
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

@Composable
private fun FlightTaskTypeRow(label: String, value: String, enabled: Boolean, errorMessage: String?, onChange: (String) -> Unit) {
    val style = MaterialTheme.typography.bodyMedium
    val options = listOf("Major" to stringResource(R.string.work_major), "Minor" to stringResource(R.string.work_minor))
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val optionWidth = options.maxOf { measurer.measure(it.second, style.copy(fontWeight = FontWeight.SemiBold)).size.width }
    val desiredWidth = maxOf(128.dp, with(density) { optionWidth.toDp() } * 2 + 32.dp)
    val labelWidth = with(density) { measurer.measure(label, style.copy(fontWeight = FontWeight.SemiBold)).size.width.toDp() }
    FlightFormValidation(errorMessage, registerError = enabled) {
        BoxWithConstraints(Modifier.fillMaxWidth()
            .alpha(if (enabled) 1f else 0.45f).padding(end = 14.dp)) {
            val selectorWidth = minOf(desiredWidth, maxWidth)
            if (labelWidth + selectorWidth + 8.dp > maxWidth) {
                Column(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(label, style = style, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    Box(Modifier.align(Alignment.End)) { FlightTaskTypeSelector(value, options, selectorWidth, enabled, onChange) }
                }
            } else Row(Modifier.fillMaxWidth().heightIn(min = 48.dp), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(label, Modifier.weight(1f), style = style, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                FlightTaskTypeSelector(value, options, selectorWidth, enabled, onChange)
            }
        }
    }
}

@Composable
private fun FlightTaskTypeSelector(value: String, options: List<Pair<String, String>>, width: Dp, enabled: Boolean, onChange: (String) -> Unit) {
    val style = MaterialTheme.typography.bodyMedium
    val faceHeight = maxOf(32.dp, with(LocalDensity.current) { style.lineHeight.toDp() } + 8.dp)
    BoxWithConstraints(Modifier.width(width).height(maxOf(48.dp, faceHeight))) {
        val segmentWidth = (maxWidth - 4.dp) / 2
        val selected = options.indexOfFirst { it.first == value }
        val slide by animateDpAsState(if (selected == 1) segmentWidth else 0.dp,
            animationSpec = tween(180), label = "taskTypeSelection")
        Box(Modifier.fillMaxWidth().height(faceHeight).align(Alignment.Center)
            .background(FlightFormGround, RoundedCornerShape(8.dp)).padding(2.dp)) {
            if (selected >= 0) Box(Modifier.offset(x = slide).width(segmentWidth).fillMaxHeight()
                .shadow(1.dp, RoundedCornerShape(6.dp)).background(MaterialTheme.colorScheme.surface, RoundedCornerShape(6.dp)))
        }
        Row(Modifier.fillMaxSize().padding(horizontal = 2.dp).selectableGroup()) {
            options.forEachIndexed { index, (type, title) ->
                Box(Modifier.weight(1f).fillMaxHeight().flightFormValidationFocus(enabled && index == 0)
                    .selectable(selected = type == value, enabled = enabled,
                    role = Role.RadioButton, onClick = { if (type != value) onChange(type) }), contentAlignment = Alignment.Center) {
                    Text(title, style = style, fontWeight = if (type == value) FontWeight.SemiBold else FontWeight.Medium,
                        color = if (type == value) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}
