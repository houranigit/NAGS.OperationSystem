package com.nags.operations.ui.flights

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.updateTransition
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.absolutePadding
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import kotlinx.coroutines.delay

private const val SEARCH_MOTION_MILLIS = 260
private val SearchSurface = Color(0xFFF2F3F5)

/** The search control grows from the trailing icon into the same row as the page tabs. */
@Composable
internal fun FlightTabSearchHeader(
    isSearchOpen: Boolean,
    query: String,
    onQueryChange: (String) -> Unit,
    onOpenSearch: () -> Unit,
    onCloseSearch: () -> Unit,
    modifier: Modifier = Modifier,
    searchDescription: String = stringResource(R.string.flights_search),
    placeholder: String = stringResource(R.string.flights_search_placeholder),
    openSearchDescription: String = stringResource(R.string.flights_search_open),
    showCloseButton: Boolean = true,
    autoFocusOnOpen: Boolean = true,
    interactionEnabled: Boolean = true,
    searchIcon: ImageVector = Icons.Default.Search,
    searchAtPhysicalRight: Boolean = false,
    searchSurface: Color = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f)
        MaterialTheme.colorScheme.surfaceContainerHigh else SearchSurface,
    tabs: @Composable () -> Unit,
) {
    val transition = updateTransition(isSearchOpen, label = "Flight search")
    val progress by transition.animateFloat(
        transitionSpec = { tween(SEARCH_MOTION_MILLIS, easing = FastOutSlowInEasing) },
        label = "Search expansion",
    ) { open -> if (open) 1f else 0f }
    val focusRequester = remember { FocusRequester() }
    val focusManager = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val foreground = MaterialTheme.colorScheme.onSurface
    val secondary = MaterialTheme.colorScheme.onSurfaceVariant
    val accent = MaterialTheme.colorScheme.primary
    val fieldVisible = isSearchOpen || progress > 0f

    LaunchedEffect(isSearchOpen, autoFocusOnOpen) {
        if (isSearchOpen && autoFocusOnOpen) {
            delay(SEARCH_MOTION_MILLIS.toLong())
            focusRequester.requestFocus()
            keyboard?.show()
        } else {
            keyboard?.hide()
            focusManager.clearFocus()
        }
    }

    fun closeSearch() {
        keyboard?.hide()
        focusManager.clearFocus()
        onCloseSearch()
    }

    Column(modifier.fillMaxWidth()) {
        HorizontalDivider(color = foreground.copy(alpha = 0.1f))
        BoxWithConstraints(Modifier.fillMaxWidth().height(48.dp)) {
            HorizontalDivider(Modifier.align(Alignment.BottomCenter), color = foreground.copy(alpha = 0.1f))
            val controlWidth = 48.dp + (maxWidth - 32.dp - 48.dp) * progress
            val tabPlacement = if (searchAtPhysicalRight) {
                Modifier.align(AbsoluteAlignment.BottomLeft).fillMaxWidth().absolutePadding(right = 72.dp)
            } else {
                Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(end = 72.dp)
            }
            val searchPlacement = if (searchAtPhysicalRight) {
                Modifier.align(AbsoluteAlignment.CenterRight).absolutePadding(right = 16.dp)
            } else {
                Modifier.align(Alignment.CenterEnd).padding(end = 16.dp)
            }
            Box(tabPlacement
                .graphicsLayer { alpha = 1f - progress }
                .then(if (isSearchOpen) Modifier.clearAndSetSemantics { } else Modifier)) {
                tabs()
            }
            Row(searchPlacement.width(controlWidth).height(48.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f).height(48.dp).clipToBounds()) {
                    Box(Modifier.fillMaxSize().padding(horizontal = 6.dp * (1f - progress), vertical = 6.dp)
                        .background(searchSurface, RoundedCornerShape(24.dp)))
                    Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically) {
                        if (isSearchOpen) {
                            Box(Modifier.size(48.dp).pointerInput(focusRequester, keyboard) {
                                detectTapGestures {
                                    focusRequester.requestFocus()
                                    keyboard?.show()
                                }
                            }, contentAlignment = Alignment.Center) {
                                Icon(searchIcon, contentDescription = null,
                                    tint = secondary, modifier = Modifier.size(21.dp))
                            }
                        } else {
                            IconButton(onClick = onOpenSearch, modifier = Modifier.size(48.dp)) {
                                Icon(searchIcon, openSearchDescription,
                                    tint = foreground, modifier = Modifier.size(22.dp))
                            }
                        }
                        if (fieldVisible) {
                            BasicTextField(value = query,
                                onValueChange = { if (isSearchOpen) onQueryChange(it) },
                                enabled = isSearchOpen && interactionEnabled, singleLine = true,
                                textStyle = MaterialTheme.typography.bodyLarge.copy(color = foreground),
                                cursorBrush = SolidColor(accent),
                                keyboardOptions = KeyboardOptions(autoCorrectEnabled = false, imeAction = ImeAction.Search),
                                keyboardActions = KeyboardActions(onSearch = {
                                    keyboard?.hide()
                                    focusManager.clearFocus()
                                }),
                                modifier = Modifier.weight(1f).fillMaxHeight()
                                    .padding(end = if (query.isEmpty()) 12.dp else 0.dp)
                                    .graphicsLayer { alpha = progress }
                                    .focusRequester(focusRequester)
                                    .semantics { contentDescription = searchDescription },
                                decorationBox = { innerTextField ->
                                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) {
                                        if (query.isEmpty()) Text(placeholder,
                                            style = MaterialTheme.typography.bodyLarge,
                                            color = secondary, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                                        innerTextField()
                                    }
                                })
                            if (query.isNotEmpty()) {
                                IconButton(onClick = {
                                    onQueryChange("")
                                    focusRequester.requestFocus()
                                    keyboard?.show()
                                }, enabled = isSearchOpen,
                                    modifier = Modifier.size(48.dp).graphicsLayer { alpha = progress }) {
                                    Icon(Icons.Default.Cancel, stringResource(R.string.flights_search_clear),
                                        tint = secondary, modifier = Modifier.size(20.dp))
                                }
                            }
                        }
                    }
                }
                if (fieldVisible && showCloseButton) {
                    Spacer(Modifier.width(8.dp * progress))
                    Box(Modifier.width(48.dp * progress).height(48.dp).clipToBounds()
                        .then(if (!isSearchOpen) Modifier.clearAndSetSemantics { } else Modifier)) {
                        IconButton(onClick = ::closeSearch, enabled = isSearchOpen,
                            modifier = Modifier.requiredSize(48.dp).graphicsLayer { alpha = progress }) {
                            Box(Modifier.size(36.dp).background(searchSurface, CircleShape), contentAlignment = Alignment.Center) {
                                Icon(Icons.Default.Close, stringResource(R.string.flights_search_close),
                                    tint = foreground, modifier = Modifier.size(20.dp))
                            }
                        }
                    }
                }
            }
        }
    }
}

/** A compact search-location capsule, positioned above the IME by its screen. */
@Composable
internal fun FlightSearchScopeTabs(
    selectedTab: Int,
    onSelectTab: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val labels = listOf(stringResource(R.string.flights_tab_my),
        stringResource(R.string.flights_tab_per_landing), stringResource(R.string.flights_tab_adhoc))
    val secondary = MaterialTheme.colorScheme.onSurfaceVariant
    val accent = MaterialTheme.colorScheme.primary
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    val scopeSurface = if (dark) MaterialTheme.colorScheme.surfaceContainerHigh else SearchSurface
    val selectedSurface = if (dark) MaterialTheme.colorScheme.surfaceContainerHighest else Color.White
    Box(modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp)) {
        Row(Modifier.fillMaxWidth().background(scopeSurface, RoundedCornerShape(26.dp))
            .padding(horizontal = 4.dp).selectableGroup(), verticalAlignment = Alignment.CenterVertically) {
            labels.forEachIndexed { index, label ->
                val selected = index == selectedTab
                Box(Modifier.weight(1f).height(48.dp)
                    .selectable(selected, onClick = { onSelectTab(index) }, role = Role.Tab),
                    contentAlignment = Alignment.Center) {
                    Box(Modifier.fillMaxSize().padding(vertical = 4.dp)
                        .background(if (selected) selectedSurface else Color.Transparent, RoundedCornerShape(22.dp)))
                    Text(label, modifier = Modifier.padding(horizontal = 8.dp), color = if (selected) accent else secondary,
                        style = MaterialTheme.typography.labelLarge.copy(fontSize = 14.sp),
                        fontWeight = FontWeight.Bold,
                        maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}
