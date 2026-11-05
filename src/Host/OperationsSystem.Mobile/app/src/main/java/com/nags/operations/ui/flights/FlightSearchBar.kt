package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandHorizontally
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkHorizontally
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.MicNone
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.sp
import com.nags.operations.R

/** A floating search capsule whose separate close control opens only while editing. */
@Composable
internal fun FlightSearchBar(
    query: String,
    onQueryChange: (String) -> Unit,
    onVoiceSearch: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val focusManager = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val focusRequester = remember { FocusRequester() }
    var focused by remember { mutableStateOf(false) }
    val placeholder = stringResource(R.string.flights_search_placeholder)
    val searchDescription = stringResource(R.string.flights_search)
    val slideDirection = if (LocalLayoutDirection.current == LayoutDirection.Ltr) 1 else -1
    val horizontalInset by animateDpAsState(
        targetValue = if (focused) 0.dp else 20.dp,
        animationSpec = spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessMediumLow),
        label = "Search bar inset",
    )
    val foreground = MaterialTheme.colorScheme.onSurface
    val fill = MaterialTheme.colorScheme.surface.copy(alpha = 0.94f)
    val border = foreground.copy(alpha = 0.2f)
    val textStyle = MaterialTheme.typography.bodyLarge.copy(fontSize = 17.sp, lineHeight = 22.sp)

    fun dismissEditing() {
        keyboard?.hide()
        focusManager.clearFocus()
    }

    // Focus owns editing state; switching keyboard modes must not dismiss the search.
    BackHandler(enabled = focused) { dismissEditing() }

    Row(modifier = modifier.padding(horizontal = horizontalInset.coerceIn(0.dp, 20.dp)),
        verticalAlignment = Alignment.CenterVertically) {
        BasicTextField(
            value = query,
            onValueChange = onQueryChange,
            singleLine = true,
            textStyle = textStyle.copy(color = foreground),
            cursorBrush = SolidColor(Color(0xFF007AFF)),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { dismissEditing() }),
            modifier = Modifier.weight(1f)
                .shadow(5.dp, CircleShape, clip = false,
                    ambientColor = Color.Black.copy(alpha = 0.08f),
                    spotColor = Color.Black.copy(alpha = 0.08f))
                .background(fill, CircleShape)
                .border(0.75.dp, border, CircleShape)
                .heightIn(min = 48.dp)
                .focusRequester(focusRequester)
                .onFocusChanged { focused = it.isFocused }
                .semantics { contentDescription = searchDescription },
            decorationBox = { innerTextField ->
                Row(
                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)
                        .padding(start = 14.dp, end = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(Icons.Default.Search, contentDescription = null,
                        tint = foreground, modifier = Modifier.size(22.dp))
                    Box(Modifier.weight(1f).padding(start = 10.dp, top = 12.dp, bottom = 12.dp)) {
                        if (query.isEmpty()) {
                            Text(placeholder, style = textStyle,
                                color = foreground.copy(alpha = 0.5f),
                                maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        innerTextField()
                    }
                    if (query.isNotEmpty()) {
                        IconButton(
                            onClick = {
                                onQueryChange("")
                                focusRequester.requestFocus()
                                keyboard?.show()
                            },
                            modifier = Modifier.size(48.dp),
                        ) {
                            Icon(Icons.Default.Cancel,
                                contentDescription = stringResource(R.string.flights_search_clear),
                                tint = foreground, modifier = Modifier.size(20.dp))
                        }
                    } else {
                        IconButton(onClick = onVoiceSearch, modifier = Modifier.size(48.dp)) {
                            Icon(Icons.Default.MicNone,
                                contentDescription = stringResource(R.string.flights_search_voice),
                                tint = foreground, modifier = Modifier.size(25.dp))
                        }
                    }
                }
            },
        )
        AnimatedVisibility(
            visible = focused,
            enter = expandHorizontally(
                animationSpec = spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessMediumLow),
                expandFrom = Alignment.End,
            ) + fadeIn(tween(150)) + slideInHorizontally(
                animationSpec = spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessMediumLow),
                initialOffsetX = { it / 4 * slideDirection },
            ),
            exit = shrinkHorizontally(
                animationSpec = spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessMediumLow),
                shrinkTowards = Alignment.End,
            ) + fadeOut(tween(120)) + slideOutHorizontally(tween(180)) { it / 4 * slideDirection },
        ) {
            Box(Modifier.padding(start = 12.dp)) {
                IconButton(
                    onClick = {
                        onQueryChange("")
                        dismissEditing()
                    },
                    modifier = Modifier.size(48.dp)
                        .shadow(5.dp, CircleShape, clip = false,
                            ambientColor = Color.Black.copy(alpha = 0.08f),
                            spotColor = Color.Black.copy(alpha = 0.08f))
                        .background(fill, CircleShape)
                        .border(0.75.dp, border, CircleShape),
                ) {
                    Icon(Icons.Default.Close,
                        contentDescription = stringResource(R.string.flights_search_close),
                        tint = foreground, modifier = Modifier.size(24.dp))
                }
            }
        }
    }
}
