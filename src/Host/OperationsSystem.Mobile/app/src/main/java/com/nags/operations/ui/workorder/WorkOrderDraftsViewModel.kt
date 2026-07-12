package com.nags.operations.ui.workorder

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import com.nags.operations.data.repo.WorkOrderDraftsRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class WorkOrderDraftsUiState(
    val search: String = "",
    val allDrafts: List<WorkOrderDraftEntity> = emptyList(),
    val filteredDrafts: List<WorkOrderDraftEntity> = emptyList(),
)

class WorkOrderDraftsViewModel(
    private val draftsRepository: WorkOrderDraftsRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(WorkOrderDraftsUiState())
    val state: StateFlow<WorkOrderDraftsUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            draftsRepository.observeDrafts().collect { drafts ->
                _state.update { st ->
                    val filtered = filterDrafts(drafts, st.search)
                    st.copy(allDrafts = drafts, filteredDrafts = filtered)
                }
            }
        }
    }

    fun setSearch(raw: String) {
        _state.update { st ->
            st.copy(search = raw, filteredDrafts = filterDrafts(st.allDrafts, raw))
        }
    }

    fun deleteDraft(draftId: String) {
        viewModelScope.launch {
            draftsRepository.deleteDraft(draftId)
        }
    }

    private fun filterDrafts(
        drafts: List<WorkOrderDraftEntity>,
        searchRaw: String,
    ): List<WorkOrderDraftEntity> {
        val q = searchRaw.trim().lowercase()
        if (q.isEmpty()) return drafts
        return drafts.filter { e ->
            e.flightNumber.lowercase().contains(q) ||
                e.customerName.lowercase().contains(q) ||
                e.stationCode.lowercase().contains(q)
        }
    }
}
