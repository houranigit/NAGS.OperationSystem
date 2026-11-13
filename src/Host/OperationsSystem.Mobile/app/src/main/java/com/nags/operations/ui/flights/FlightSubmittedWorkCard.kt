package com.nags.operations.ui.flights

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Groups
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.InsertDriveFile
import androidx.compose.material.icons.outlined.List
import androidx.compose.material.icons.outlined.Mic
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material.icons.outlined.Videocam
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.theme.AccentBlue
import com.nags.operations.ui.util.parseInUserZone
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale

/** Published or durably queued work; new and unstarted drafts retain their inline editor. */
@Composable
internal fun FlightSubmittedServiceCard(
    service: FlightServiceDraft,
    metadata: FlightWorkRecordMetadata,
    zone: ZoneId,
    onUpdate: (() -> Unit)? = null,
    onAttachment: ((FlightAttachmentDraft) -> Unit)? = null,
    attachmentContent: @Composable ((FlightAttachmentDraft) -> Unit)? = null,
    modifier: Modifier = Modifier,
) {
    SubmittedWorkCard("service:${service.id}", service.serviceName.ifBlank { "—" }, service.period,
        service.reference, "", metadata, zone, isTask = false,
        hasDetails = service.employees.isNotEmpty() || service.remarks.isNotBlank() || service.attachments.isNotEmpty(),
        onUpdate = onUpdate, modifier = modifier) { stackItems ->
        SubmittedWorkDetails(service.employees, emptyList(), service.period, service.remarks, service.attachments,
            isTask = false, stackItems = stackItems, onAttachment = onAttachment, attachmentContent = attachmentContent)
    }
}

@Composable
internal fun FlightSubmittedTaskCard(
    task: FlightTaskDraft,
    taskNumber: Int,
    metadata: FlightWorkRecordMetadata,
    zone: ZoneId,
    onUpdate: (() -> Unit)? = null,
    onAttachment: ((FlightAttachmentDraft) -> Unit)? = null,
    attachmentContent: @Composable ((FlightAttachmentDraft) -> Unit)? = null,
    modifier: Modifier = Modifier,
) {
    val type = when (task.taskType) {
        "Major" -> stringResource(R.string.work_major)
        "Minor" -> stringResource(R.string.work_minor)
        else -> ""
    }
    val name = listOf(stringResource(R.string.work_task_number, taskNumber), type).filter { it.isNotBlank() }.joinToString(" · ")
    SubmittedWorkCard("task:${task.id}", name, task.period, task.reference,
        listOf(task.ataChapterCode, task.ataChapterTitle).filter { it.isNotBlank() }.joinToString(" · "),
        metadata, zone, isTask = true,
        hasDetails = task.employees.isNotEmpty() || task.tools.isNotEmpty() || task.materials.isNotEmpty() ||
            task.generalSupports.isNotEmpty() || task.remarks.isNotBlank() || task.attachments.isNotEmpty(),
        onUpdate = onUpdate, modifier = modifier) { stackItems ->
        SubmittedWorkDetails(task.employees, listOf(
            SubmittedResourceGroup(R.string.work_tools, task.tools),
            SubmittedResourceGroup(R.string.work_materials, task.materials),
            SubmittedResourceGroup(R.string.work_general_support, task.generalSupports),
        ), task.period, task.remarks, task.attachments, isTask = true, stackItems = stackItems,
            onAttachment = onAttachment, attachmentContent = attachmentContent)
    }
}

@Composable
private fun SubmittedWorkCard(
    navigationKey: String,
    name: String,
    period: FlightWorkPeriod,
    reference: String,
    chapter: String,
    metadata: FlightWorkRecordMetadata,
    zone: ZoneId,
    isTask: Boolean,
    hasDetails: Boolean,
    onUpdate: (() -> Unit)?,
    modifier: Modifier,
    details: @Composable (stackItems: Boolean) -> Unit,
) {
    var expanded by rememberSaveable(navigationKey) { mutableStateOf(false) }
    val caption = MaterialTheme.colorScheme.onSurfaceVariant
    val detailsLabel = stringResource(R.string.work_submitted_view_details)
    val disclosureState = stringResource(if (expanded) R.string.flights_section_expanded else R.string.flights_section_collapsed)
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
            Surface(Modifier.fillMaxWidth().flightFormNavigationTarget(navigationKey, highlight = true),
                shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface) {
                BoxWithConstraints(Modifier.fillMaxWidth().padding(12.dp)) {
                    val availableWidth = maxWidth
                    val density = LocalDensity.current
                    val measurer = rememberTextMeasurer()
                    val labelStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.SemiBold)
                    val timeStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 18.sp, lineHeight = 22.sp,
                        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
                    val captionStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.Medium)
                    val locale = LocalLocale.current.platformLocale
                    val start = submittedClock(period.start, locale)
                    val end = submittedClock(period.end, locale)
                    val dates = submittedDates(period, locale)
                    val startLabel = stringResource(R.string.work_start)
                    val endLabel = stringResource(R.string.work_end)
                    val cellWidth = with(density) {
                        maxOf(measurer.measure(start, timeStyle).size.width, measurer.measure(end, timeStyle).size.width,
                            measurer.measure(startLabel, labelStyle).size.width, measurer.measure(endLabel, labelStyle).size.width).toDp()
                    }
                    val timesWidth = cellWidth * 2 + 26.dp
                    val datesWidth = if (dates.isBlank()) 0.dp else with(density) { measurer.measure(dates, captionStyle).size.width.toDp() } + 18.dp
                    val timingWidth = maxOf(timesWidth, datesWidth).coerceAtMost(maxWidth)
                    val gap = if (maxWidth < 260.dp) 8.dp else 16.dp
                    val stackTop = timingWidth + gap + 76.dp * density.fontScale > maxWidth
                    val info: @Composable (Modifier) -> Unit = { infoModifier ->
                        Column(infoModifier, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            Text(name, Modifier.semantics { heading() }, style = labelStyle, color = MaterialTheme.colorScheme.onSurface)
                            SubmittedWorkStatus(period.end.isNotBlank())
                            if (metadata.queued) SubmittedWorkPending(metadata.pendingStatus)
                            if (reference.isNotBlank() || chapter.isNotBlank()) Column(
                                Modifier.padding(top = 2.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                if (reference.isNotBlank()) SubmittedMetadata(stringResource(R.string.work_customer_reference), reference)
                                if (chapter.isNotBlank()) SubmittedMetadata(stringResource(R.string.work_submitted_ata), chapter)
                            }
                        }
                    }
                    val timing: @Composable (Modifier) -> Unit = { timingModifier ->
                        Column(timingModifier.width(timingWidth), horizontalAlignment = Alignment.End,
                            verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Row(Modifier.width(timesWidth), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.Bottom) {
                                Column(Modifier.width(cellWidth), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                    Text(startLabel, style = labelStyle, color = caption, maxLines = 1, softWrap = false)
                                    Text(start, style = timeStyle, maxLines = 1, softWrap = false)
                                }
                                Icon(Icons.AutoMirrored.Filled.ArrowForward, null, Modifier.padding(bottom = 4.dp).size(14.dp), tint = caption)
                                Column(Modifier.width(cellWidth), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                    Text(endLabel, style = labelStyle, color = caption, maxLines = 1, softWrap = false)
                                    Text(end, style = timeStyle, maxLines = 1, softWrap = false)
                                }
                            }
                            if (dates.isNotBlank()) Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.End),
                                verticalAlignment = Alignment.Top) {
                                Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp), tint = caption)
                                Text(dates, Modifier.weight(1f, fill = false), style = captionStyle, color = caption, textAlign = TextAlign.End)
                            }
                        }
                    }
                    Column(Modifier.fillMaxWidth()) {
                        if (stackTop) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            info(Modifier.fillMaxWidth())
                            timing(Modifier.align(Alignment.End))
                        } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(gap), verticalAlignment = Alignment.Top) {
                            info(Modifier.weight(1f))
                            timing(Modifier)
                        }
                        if (hasDetails) {
                            HorizontalDivider(Modifier.padding(top = 8.dp), thickness = 0.5.dp,
                                color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
                            Row(Modifier.fillMaxWidth().heightIn(min = 48.dp)
                                .clickable(role = Role.Button, onClickLabel = "$detailsLabel: $name") { expanded = !expanded }
                                .semantics { stateDescription = disclosureState },
                                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                Icon(if (isTask) Icons.Outlined.List else Icons.Outlined.Groups, null, Modifier.size(14.dp), tint = caption)
                                Text(detailsLabel, Modifier.weight(1f), style = labelStyle, color = caption)
                                Icon(if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, null, Modifier.size(18.dp), tint = caption)
                            }
                            if (expanded) details(availableWidth < 300.dp)
                        }
                    }
                }
            }
        }
        SubmittedWorkNote(metadata, zone, isTask, onUpdate)
    }
}

@Composable
private fun SubmittedWorkStatus(done: Boolean) {
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    val foreground = if (done) { if (dark) Color(0xFFA3DFB7) else Color(0xFF176A3A) }
        else { if (dark) Color(0xFFA4CBFF) else Color(0xFF155EAB) }
    val background = if (done) { if (dark) Color(0xFF233C2D) else Color(0xFFE8F5EC) }
        else { if (dark) Color(0xFF21334B) else Color(0xFFE8F2FF) }
    Surface(shape = RoundedCornerShape(50), color = background) {
        Text(stringResource(if (done) R.string.work_submitted_done else R.string.work_submitted_active),
            Modifier.padding(horizontal = 7.dp, vertical = 2.dp),
            style = MaterialTheme.typography.bodySmall.copy(fontSize = 11.sp, lineHeight = 15.sp),
            fontWeight = FontWeight.SemiBold, color = foreground)
    }
}

@Composable
private fun SubmittedWorkPending(status: String?) {
    val review = status != null && status != "pending"
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    val color = if (review) MaterialTheme.colorScheme.error else if (dark) Color(0xFFFFD18E) else Color(0xFF8B4B00)
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        Icon(if (review) Icons.Outlined.WarningAmber else Icons.Outlined.Sync, null, Modifier.size(14.dp), tint = color)
        Text(stringResource(if (review) R.string.flight_card_needs_review else R.string.flight_card_pending_sync),
            style = MaterialTheme.typography.bodySmall.copy(fontSize = 11.sp, lineHeight = 15.sp), color = color)
    }
}

@Composable
private fun SubmittedMetadata(label: String, value: String, modifier: Modifier = Modifier) {
    val caption = MaterialTheme.colorScheme.onSurfaceVariant
    val foreground = MaterialTheme.colorScheme.onSurface
    Text(buildAnnotatedString {
        withStyle(SpanStyle(color = caption)) { append(label) }
        append(" ")
        withStyle(SpanStyle(color = foreground)) { append(value) }
    }, modifier, style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp), fontWeight = FontWeight.Medium)
}

private data class SubmittedResourceGroup(val title: Int, val items: List<FlightResourceDraft>)

@Composable
private fun SubmittedWorkDetails(
    employees: List<FlightEmployeeDraft>,
    groups: List<SubmittedResourceGroup>,
    parentPeriod: FlightWorkPeriod,
    remarks: String,
    attachments: List<FlightAttachmentDraft>,
    isTask: Boolean,
    stackItems: Boolean,
    onAttachment: ((FlightAttachmentDraft) -> Unit)?,
    attachmentContent: @Composable ((FlightAttachmentDraft) -> Unit)?,
) {
    Column(Modifier.fillMaxWidth().padding(bottom = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        if (employees.isNotEmpty()) SubmittedWorkGroup(stringResource(R.string.work_employees, employees.size)) {
            employees.forEach { employee ->
                SubmittedWorkItem(employee.employeeName, employee.period, null, employee.remarks, parentPeriod, stackItems)
            }
        }
        groups.filter { it.items.isNotEmpty() }.forEach { group ->
            SubmittedWorkGroup(stringResource(group.title, group.items.size)) {
                group.items.forEach { resource ->
                    SubmittedWorkItem(resource.resourceName,
                        resource.period.takeIf { resource.calculationType == 1 || it.start.isNotBlank() || it.end.isNotBlank() },
                        resource.quantity.takeIf { resource.calculationType != 1 && resource.period.start.isBlank() && resource.period.end.isBlank() },
                        resource.remarks, parentPeriod, stackItems)
                }
            }
        }
        if (remarks.isNotBlank()) {
            val label = stringResource(if (isTask) R.string.work_task_remarks_heading else R.string.work_service_remarks_heading)
            Text(buildAnnotatedString {
                append(label); append(": ")
                withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append("“$remarks”") }
            }, style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        attachments.forEach { attachment ->
            if (attachmentContent != null) attachmentContent(attachment)
            else SubmittedWorkAttachment(attachment, onAttachment)
        }
    }
}

@Composable
private fun SubmittedWorkGroup(title: String, content: @Composable () -> Unit) {
    val rule = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.65f)
    Column(Modifier.fillMaxWidth().padding(start = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(title, style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp),
            color = MaterialTheme.colorScheme.onSurfaceVariant, fontWeight = FontWeight.SemiBold)
        Column(Modifier.fillMaxWidth().drawBehind {
            val stroke = 0.75.dp.toPx()
            drawLine(rule, Offset(stroke / 2, 0f), Offset(stroke / 2, size.height), strokeWidth = stroke, cap = StrokeCap.Round)
        }.padding(start = 10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) { content() }
    }
}

@Composable
private fun SubmittedWorkItem(name: String, period: FlightWorkPeriod?, quantity: String?, remarks: String,
    parentPeriod: FlightWorkPeriod, stack: Boolean) {
    val locale = LocalLocale.current.platformLocale
    val style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp)
    val caption = MaterialTheme.colorScheme.onSurfaceVariant
    val value: @Composable (Modifier) -> Unit = { modifier ->
        if (period != null) Row(modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(submittedClock(period.start, locale), style = style, fontWeight = FontWeight.Medium, color = caption,
                maxLines = 1, softWrap = false)
            Icon(Icons.AutoMirrored.Filled.ArrowForward, null, Modifier.size(12.dp), tint = caption)
            Text(submittedClock(period.end, locale), style = style, fontWeight = FontWeight.Medium, color = caption,
                maxLines = 1, softWrap = false)
        } else if (!quantity.isNullOrBlank()) SubmittedMetadata(stringResource(R.string.work_quantity), quantity, modifier)
    }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(3.dp)) {
        if (stack) {
            Text(name.ifBlank { "—" }, style = style, fontWeight = FontWeight.SemiBold)
            value(Modifier)
        } else BoxWithConstraints(Modifier.fillMaxWidth()) {
            val availableWidth = maxWidth
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.Top) {
                Text(name.ifBlank { "—" }, Modifier.weight(1f), style = style, fontWeight = FontWeight.SemiBold)
                value(Modifier.widthIn(max = availableWidth * 0.6f))
            }
        }
        if (period != null && (submittedLocal(period.start)?.toLocalDate() != submittedLocal(parentPeriod.start)?.toLocalDate() ||
                submittedLocal(period.end)?.toLocalDate()?.let { it != submittedLocal(period.start)?.toLocalDate() } == true)) {
            submittedDates(period, locale).takeIf { it.isNotBlank() }?.let {
                Text(it, style = style, color = caption)
            }
        }
        if (remarks.isNotBlank()) Text(remarks, style = style, color = caption, fontStyle = FontStyle.Italic)
    }
}

@Composable
private fun SubmittedWorkAttachment(item: FlightAttachmentDraft, onAttachment: ((FlightAttachmentDraft) -> Unit)?) {
    val icon = when {
        item.mimeType.startsWith("image/") -> Icons.Outlined.Image
        item.mimeType.startsWith("audio/") -> Icons.Outlined.Mic
        item.mimeType.startsWith("video/") -> Icons.Outlined.Videocam
        else -> Icons.Outlined.InsertDriveFile
    }
    Row(Modifier.fillMaxWidth().heightIn(min = if (onAttachment == null) 20.dp else 48.dp)
        .then(if (onAttachment == null) Modifier else Modifier.clickable(enabled = item.uri.isNotBlank(), role = Role.Button) { onAttachment(item) }),
        horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(item.name.ifBlank { stringResource(R.string.work_attachment_name) }, style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun SubmittedWorkNote(metadata: FlightWorkRecordMetadata, zone: ZoneId, isTask: Boolean, onUpdate: (() -> Unit)?) {
    val locale = LocalLocale.current.platformLocale
    val template = stringResource(R.string.flight_submission_timestamp)
    val createdAt = if (metadata.published) metadata.createdAtUtc else metadata.savedAtUtc.takeIf { metadata.queued }
    val timestamp = remember(createdAt, zone, locale, template) {
        createdAt?.let { raw -> runCatching {
            val value = parseInUserZone(raw, zone)
            String.format(locale, template, value.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)),
                value.format(DateTimeFormatter.ofPattern("HH:mm", locale)))
        }.getOrNull() }
    }
    val prefix = when {
        metadata.published -> stringResource(if (isTask) R.string.work_submitted_note_task else R.string.work_submitted_note_service)
        metadata.queued -> stringResource(R.string.flight_actuals_saved_locally)
        else -> ""
    }
    val author = metadata.createdByDisplayName?.takeIf { metadata.published && it.isNotBlank() }
    val by = stringResource(R.string.work_submitted_note_by)
    val on = stringResource(R.string.flight_submission_note_on)
    val needsReview = metadata.pendingStatus != null && metadata.pendingStatus != "pending"
    val update = stringResource(if (needsReview) R.string.flight_review_action else if (isTask) R.string.work_submitted_update_task else R.string.work_submitted_update_service)
    val linkColor = if (needsReview) MaterialTheme.colorScheme.error else if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFF70B7FF) else AccentBlue
    if (prefix.isNotBlank() || onUpdate != null) Text(buildAnnotatedString {
        append(prefix)
        if (author != null) { append(by); withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(author) } }
        if (timestamp != null) { append(on); withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(timestamp) } }
        if (prefix.isNotBlank()) append(".")
        if (onUpdate != null) {
            if (prefix.isNotBlank()) append(" ")
            withLink(LinkAnnotation.Clickable("update-work", TextLinkStyles(style = SpanStyle(color = linkColor))) { onUpdate() }) { append(update) }
        }
    }, Modifier.fillMaxWidth().padding(horizontal = 14.dp),
        style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}

private fun submittedLocal(value: String): LocalDateTime? = runCatching { LocalDateTime.parse(value) }.getOrNull()

private fun submittedClock(value: String, locale: Locale): String = submittedLocal(value)
    ?.format(DateTimeFormatter.ofPattern("HH:mm", locale)) ?: "—"

private fun submittedDates(period: FlightWorkPeriod, locale: Locale): String {
    val start = submittedLocal(period.start)?.toLocalDate()
    val end = submittedLocal(period.end)?.toLocalDate()
    val formatter = DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)
    return when {
        start != null && end != null && start != end -> "${start.format(formatter)} – ${end.format(formatter)}"
        start != null -> start.format(formatter)
        end != null -> end.format(formatter)
        else -> ""
    }
}
