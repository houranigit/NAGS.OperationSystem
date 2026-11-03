package com.nags.operations.data.repo

import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightPage
import com.nags.operations.data.api.OperationsApi

/** Flights are read online; each refresh and detail open uses the current server scope. */
class FlightsRepository(private val api: OperationsApi) {
    suspend fun flights(page: Int = 1, pageSize: Int = 20, search: String? = null): FlightPage =
        api.flights(page, pageSize, search)

    suspend fun flight(id: String): FlightDetail = api.flight(id)

    suspend fun invitationOptions(id: String): List<AssignedEmployee> = api.invitationOptions(id)

    suspend fun invite(id: String, staffMemberIds: List<String>, rowVersion: String) =
        api.invite(id, staffMemberIds, rowVersion)
}
