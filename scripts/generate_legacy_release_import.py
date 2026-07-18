#!/usr/bin/env python3
"""Generate a review-first SQL Server import from the loaded legacy MySQL backup.

The generated SQL is deliberately rollback-by-default and contains validation gates for every
known incompatibility. It never creates Identity users: legacy operation_staff rows become
masterdata.staff_members with no linked portal user.
"""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import uuid
from pathlib import Path
from typing import Any, Iterable, Sequence


COUNTRY_ISO_BY_LEGACY_ID = {
    8: "SA", 11: "LB", 37: "JO", 72: "AE", 73: "DZ", 74: "BH", 75: "BD",
    77: "EG", 78: "ET", 79: "IN", 80: "MY", 81: "YE", 82: "GB", 83: "AE",
    84: "TN", 85: "TH", 86: "SY", 87: "SD", 88: "SG", 89: "PH", 90: "PK",
    91: "NG", 92: "MA", 93: "ID", 94: "FR", 103: "OM", 105: "TR", 107: "LY",
    108: "MV", 111: "IR", 144: "CM", 145: "IQ", 146: "GH", 147: "LT",
    148: "PL", 149: "PT", 151: "VN", 152: "NP", 156: "BG", 157: "AF",
    159: "MT", 160: "RU", 161: "ES", 162: "GR", 163: "US", 164: "LK",
    165: "KW", 166: "UA", 167: "LV", 169: "HU", 170: "GE", 171: "UZ",
    172: "AZ", 173: "QA", 174: "IT", 175: "IE", 176: "DE",
}

# Source station rows do not contain airport codes. These five mappings are inferred from their
# names and are intentionally visible in the generated SQL for review. Head Office remains blocked.
STATION_IMPORT_MAPPING = {
    3: ("JED", None, None, "SA"),
    4: ("RUH", None, None, "SA"),
    7: ("DMM", None, None, "SA"),
    11: ("MED", None, None, "SA"),
    12: ("HOF", None, None, "SA"),
    13: ("TIF", None, None, "SA"),
}

# Approved customer-code decisions. A null IATA means that the customer does not have an IATA code.
CUSTOMER_CODE_OVERRIDES = {
    68: ("WY", "OMA"),    # Oman Air
    106: ("NE", "NMA"),   # Nesma Airlines Egypt
    111: (None, "ROJ"),   # Royal Jet
    133: (None, "TVR"),   # Terra Avia
    153: (None, "SVA"),   # Saudia Private Aviation
    163: (None, "XLR"),   # Texel Air
    168: ("Z7", "CMS"),   # Camex Airlines
    169: ("YI", "OYA"),   # Fly Oya
    175: (None, "MLT"),   # Maleth Aero
    180: ("AH", "GJM"),   # Airhub Airlines
    237: ("8D", "SJO"),   # SolitAir Aviation
}

# These two source values both normalize to 9P and are intentionally retained. Customer IATA
# codes are optional and non-unique in the revised application model.
APPROVED_DUPLICATE_IATA_CUSTOMER_IDS = {72, 212}

# Valid source records that are intentionally excluded after review.
APPROVED_EXCLUDED_CUSTOMER_IDS = {
    99: "Rejected by user: NESMA AIRLINES duplicates the kept NESMA AIRLINES EGYPT ICAO",
}

# For each duplicate operational-staff email, retain the user-selected source record and discard
# the other source records in that email group.
APPROVED_DUPLICATE_STAFF_IDS = {118, 120, 134}

# Approved replacements for the two 250-character source addresses.
CUSTOMER_ADDRESS_OVERRIDES = {
    88: "Mitiga International Airport, Souq al-Jum'aa, Tripoli, Libya",
    100: "Krakowiakow str. 48, Warsaw 02-255, Poland",
}

WELL_KNOWN_OPERATION_TYPE_IDS = {
    21: "30000000-0000-0000-0000-000000000001",  # Adhoc
}

WELL_KNOWN_SERVICE_IDS = {
    35: "40000000-0000-0000-0000-000000000001",  # Aircraft Per landing Maintenance Service
}

# On Call is a derived Operations state, not a catalog service. Keeping this explicit prevents a
# reset/import from recreating the retired legacy service row.
RETIRED_LEGACY_SERVICE_IDS = {33}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mysql-container", default="nags-legacy-import")
    parser.add_argument("--mysql-database", default="nagsoperation")
    parser.add_argument("--mysql-user", default="root")
    parser.add_argument("--dump", type=Path, help="Load source data directly from Dump20260628.sql instead of Docker/MySQL.")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--apply-default",
        action="store_true",
        help="Generate a direct-run seeder with @Apply=1 by default instead of a rollback-first review draft.",
    )
    return parser.parse_args()


def mysql_json(args: argparse.Namespace, query: str) -> list[dict[str, Any]]:
    password = os.environ.get("LEGACY_MYSQL_PASSWORD", "legacy-import")
    command = [
        "docker", "exec", "-i", "-e", f"MYSQL_PWD={password}", args.mysql_container,
        "mysql", f"-u{args.mysql_user}", "--default-character-set=utf8mb4", "--batch", "--raw",
        "--skip-column-names", args.mysql_database,
    ]
    result = subprocess.run(command, input=query, text=True, capture_output=True, check=True)
    return [json.loads(line) for line in result.stdout.splitlines() if line.strip()]


def dotnet_guid(kind: str, legacy_id: Any) -> str:
    digest = hashlib.md5(f"legacy-release:{kind}:{legacy_id}".encode("utf-8")).digest()
    return str(uuid.UUID(bytes_le=digest))


def operation_type_guid(source_id: Any) -> str:
    return WELL_KNOWN_OPERATION_TYPE_IDS.get(int(source_id), dotnet_guid("operation-type", source_id))


def service_guid(source_id: Any) -> str:
    return WELL_KNOWN_SERVICE_IDS.get(int(source_id), dotnet_guid("service", source_id))


def normalized(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def utc(value: Any) -> str | None:
    text = normalized(value)
    if not text:
        return None
    if len(text) > 10 and text[10] == " ":
        text = f"{text[:10]}T{text[11:]}"
    return f"{text}+00:00"


def sql(value: Any) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, (int, float)):
        return str(value)
    return "N'" + str(value).replace("'", "''") + "'"


def insert_values(table: str, columns: Sequence[str], rows: Iterable[Sequence[Any]]) -> str:
    materialized = list(rows)
    if not materialized:
        return ""
    values = ",\n".join("    (" + ", ".join(sql(value) for value in row) + ")" for row in materialized)
    return f"INSERT INTO {table} ({', '.join(columns)}) VALUES\n{values};\n"


def is_valid_iata(value: str | None) -> bool:
    return value is None or (len(value) == 2 and value.isascii() and value.isalnum())


def is_valid_icao(value: str | None) -> bool:
    return value is None or (len(value) == 3 and value.isascii() and value.isalpha())


def parse_mysql_string(content: str, index: int) -> tuple[str, int]:
    if content[index] != "'":
        raise ValueError(f"Expected quoted string at offset {index}")

    index += 1
    chars: list[str] = []
    escapes = {
        "0": "\0",
        "b": "\b",
        "n": "\n",
        "r": "\r",
        "t": "\t",
        "Z": "\x1a",
    }
    while index < len(content):
        char = content[index]
        if char == "\\":
            index += 1
            if index >= len(content):
                break
            escaped = content[index]
            chars.append(escapes.get(escaped, escaped))
            index += 1
            continue

        if char == "'":
            if index + 1 < len(content) and content[index + 1] == "'":
                chars.append("'")
                index += 2
                continue

            return "".join(chars), index + 1

        chars.append(char)
        index += 1

    raise ValueError("Unclosed MySQL string literal")


def parse_mysql_insert_rows(content: str, table: str) -> list[list[Any]]:
    marker = f"INSERT INTO `{table}` VALUES"
    rows: list[list[Any]] = []
    position = 0

    while True:
        position = content.find(marker, position)
        if position < 0:
            return rows

        index = position + len(marker)
        while index < len(content):
            while index < len(content) and content[index].isspace():
                index += 1

            if index >= len(content):
                raise ValueError(f"Unexpected end while parsing {table}")

            if content[index] == ";":
                index += 1
                break

            if content[index] == ",":
                index += 1
                continue

            if content[index] != "(":
                raise ValueError(f"Expected row start while parsing {table} at offset {index}: {content[index:index + 30]!r}")

            index += 1
            row: list[Any] = []
            while True:
                while index < len(content) and content[index].isspace():
                    index += 1

                if content.startswith("_binary", index):
                    index += len("_binary")
                    while index < len(content) and content[index].isspace():
                        index += 1
                    value, index = parse_mysql_string(content, index)
                elif content[index] == "'":
                    value, index = parse_mysql_string(content, index)
                else:
                    end = index
                    while end < len(content) and content[end] not in ",)":
                        end += 1
                    token = content[index:end].strip()
                    value = None if token.upper() == "NULL" else token
                    index = end

                row.append(value)

                while index < len(content) and content[index].isspace():
                    index += 1

                if content[index] == ",":
                    index += 1
                    continue

                if content[index] == ")":
                    index += 1
                    break

                raise ValueError(f"Expected value separator while parsing {table} at offset {index}: {content[index:index + 30]!r}")

            rows.append(row)

        position = index


def parse_mysql_columns(content: str, table: str) -> list[str]:
    marker = f"CREATE TABLE `{table}`"
    start = content.find(marker)
    if start < 0:
        raise ValueError(f"Table {table} was not found in dump")

    end = content.find(") ENGINE=", start)
    if end < 0:
        raise ValueError(f"Table {table} create statement was not terminated in dump")

    create_statement = content[start:end]
    return re.findall(r"^\s*`([^`]+)`\s+", create_statement, flags=re.MULTILINE)


def dump_table(content: str, table: str) -> list[dict[str, Any]]:
    columns = parse_mysql_columns(content, table)
    rows = parse_mysql_insert_rows(content, table)
    records = []
    for index, row in enumerate(rows, start=1):
        if len(row) != len(columns):
            raise ValueError(
                f"Table {table} row {index} has {len(row)} values but {len(columns)} columns"
            )
        records.append(dict(zip(columns, row)))
    return records


def manufacturer_from_legacy(lkp_by_id: dict[int, dict[str, Any]], manufacturer_id: Any) -> str:
    name = normalized(lkp_by_id.get(int(manufacturer_id), {}).get("NAME_EN"))
    if name is None:
        return "Other"

    normalized_name = name.strip().lower()
    if "airbus" in normalized_name:
        return "Airbus"
    if "boeing" in normalized_name:
        return "Boeing"
    if "embraer" in normalized_name:
        return "Embraer"
    if "atr" in normalized_name:
        return "ATR"
    if "bombardier" in normalized_name:
        return "Bombardier"
    return "Other"


def load_source_from_dump(path: Path) -> dict[str, list[dict[str, Any]]]:
    content = path.read_text(encoding="utf-8", errors="replace")
    tables = {
        name: dump_table(content, name)
        for name in [
            "license", "manpower", "station", "customer", "user", "operation_staff",
            "operation_staff_license", "customer_license", "service", "operation_type",
            "airplane", "tool", "tool_equipment", "material", "general_support", "lkp",
        ]
    }

    operation_staff_by_id = {int(row["ID"]): row for row in tables["operation_staff"]}
    users = sorted(tables["user"], key=lambda row: int(row["ID"]))
    lkp_by_id = {int(row["ID"]): row for row in tables["lkp"]}

    data: dict[str, list[dict[str, Any]]] = {
        "licenses": [
            {
                "id": row["ID"],
                "code": row["CODE"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["license"], key=lambda row: int(row["ID"]))
        ],
        "manpower": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["manpower"], key=lambda row: int(row["ID"]))
        ],
        "stations": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "note": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["station"], key=lambda row: int(row["ID"]))
        ],
        "customers": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "country_id": row["COUNTRY_ID"],
                "iata": row["IATA_CODE"],
                "icao": row["ICAO_CODE"],
                "email": row["CUSTOMER_EMAIL"],
                "phone": row["CUSTOMER_TEL"],
                "address": row["CUSTOMER_ADDRESS"],
                "city": row["CUSTOMER_CITY"],
                "pobox": row["CUSTOMER_POBOX"],
                "postal": row["CUSTOMER_POSTAL_CODE"],
                "contact_name": row["CONTACT_PERSON"],
                "contact_email": row["CONTACT_PERSON_EMAIL"],
                "contact_phone": row["CONTACT_PERSON_TEL"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["customer"], key=lambda row: int(row["ID"]))
        ],
        "staff": [
            {
                "id": user["ID"],
                "employee_id": user["EMP_ID"],
                "name": user["NAME_EN"],
                "email": user["EMAIL_ADDRESS"],
                "station_id": user["STATION_ID"],
                "manpower_id": operation_staff_by_id[int(user["ID"])]["MANPOWER_ID"],
                "license_number": operation_staff_by_id[int(user["ID"])]["LIC_ID"],
                "status_id": user["STATUS_ID"],
                "created": user["CREATION_DATE"],
                "updated": user["UPDATE_DATE"],
            }
            for user in users if int(user["ID"]) in operation_staff_by_id
        ],
        "staff_licenses": [
            {
                "user_id": row["USER_ID"],
                "license_id": row["LICENSE_ID"],
                "license_number": operation_staff_by_id[int(row["USER_ID"])]["LIC_ID"],
            }
            for row in sorted(tables["operation_staff_license"], key=lambda row: (int(row["USER_ID"]), int(row["LICENSE_ID"])))
        ],
        "customer_licenses": [
            {"customer_id": row["CUSTOMER_ID"], "license_id": row["LICENSE_ID"]}
            for row in sorted(tables["customer_license"], key=lambda row: (int(row["CUSTOMER_ID"]), int(row["LICENSE_ID"])))
        ],
        "user_only": [
            {
                "id": user["ID"],
                "employee_id": user["EMP_ID"],
                "name": user["NAME_EN"],
                "email": user["EMAIL_ADDRESS"],
                "station_id": user["STATION_ID"],
                "status_id": user["STATUS_ID"],
            }
            for user in users if int(user["ID"]) not in operation_staff_by_id
        ],
        "services": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["service"], key=lambda row: int(row["ID"]))
        ],
        "operation_types": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["operation_type"], key=lambda row: int(row["ID"]))
        ],
        "aircraft_types": [
            {
                "id": row["ID"],
                "manufacturer": manufacturer_from_legacy(lkp_by_id, row["MANUFACTURER_ID"]),
                "model": row["CODE"],
                "notes": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["airplane"], key=lambda row: int(row["ID"]))
        ],
        "tools": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["tool"], key=lambda row: int(row["ID"]))
        ],
        "tool_equipments": [
            {
                "id": row["ID"],
                "tool_id": row["TOOL_ID"],
                "factory_id": row["FACTORY_ID"],
                "serial_id": row["SERIAL_ID"],
                "calibration_date": row["CALIBRATION_DATE"],
            }
            for row in sorted(tables["tool_equipment"], key=lambda row: int(row["ID"]))
        ],
        "materials": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["material"], key=lambda row: int(row["ID"]))
        ],
        "general_supports": [
            {
                "id": row["ID"],
                "name": row["NAME_EN"],
                "description": row["NOTE"],
                "created": row["CREATION_DATE"],
                "updated": row["UPDATE_DATE"],
            }
            for row in sorted(tables["general_support"], key=lambda row: int(row["ID"]))
        ],
    }

    return data


def load_source(args: argparse.Namespace) -> dict[str, list[dict[str, Any]]]:
    if args.dump is not None:
        return load_source_from_dump(args.dump)

    queries = {
        "licenses": """
            SELECT JSON_OBJECT('id',ID,'code',CODE,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM license ORDER BY ID;
        """,
        "manpower": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM manpower ORDER BY ID;
        """,
        "stations": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'note',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM station ORDER BY ID;
        """,
        "customers": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'country_id',COUNTRY_ID,
              'iata',IATA_CODE,'icao',ICAO_CODE,'email',CUSTOMER_EMAIL,'phone',CUSTOMER_TEL,
              'address',CUSTOMER_ADDRESS,'city',CUSTOMER_CITY,'pobox',CUSTOMER_POBOX,
              'postal',CUSTOMER_POSTAL_CODE,'contact_name',CONTACT_PERSON,
              'contact_email',CONTACT_PERSON_EMAIL,'contact_phone',CONTACT_PERSON_TEL,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM customer ORDER BY ID;
        """,
        "staff": """
            SELECT JSON_OBJECT('id',u.ID,'employee_id',u.EMP_ID,'name',u.NAME_EN,
              'email',u.EMAIL_ADDRESS,'station_id',u.STATION_ID,'manpower_id',os.MANPOWER_ID,
              'license_number',os.LIC_ID,'status_id',u.STATUS_ID,
              'created',DATE_FORMAT(u.CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(u.UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM user u JOIN operation_staff os ON os.ID=u.ID ORDER BY u.ID;
        """,
        "staff_licenses": """
            SELECT JSON_OBJECT('user_id',osl.USER_ID,'license_id',osl.LICENSE_ID,
              'license_number',os.LIC_ID)
            FROM operation_staff_license osl JOIN operation_staff os ON os.ID=osl.USER_ID
            ORDER BY osl.USER_ID,osl.LICENSE_ID;
        """,
        "customer_licenses": """
            SELECT JSON_OBJECT('customer_id',CUSTOMER_ID,'license_id',LICENSE_ID)
            FROM customer_license ORDER BY CUSTOMER_ID,LICENSE_ID;
        """,
        "user_only": """
            SELECT JSON_OBJECT('id',u.ID,'employee_id',u.EMP_ID,'name',u.NAME_EN,
              'email',u.EMAIL_ADDRESS,'station_id',u.STATION_ID,'status_id',u.STATUS_ID)
            FROM user u LEFT JOIN operation_staff os ON os.ID=u.ID
            WHERE os.ID IS NULL ORDER BY u.ID;
        """,
        "services": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM service ORDER BY ID;
        """,
        "operation_types": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM operation_type ORDER BY ID;
        """,
        "aircraft_types": """
            SELECT JSON_OBJECT('id',a.ID,'manufacturer',
                CASE
                    WHEN LOWER(l.NAME_EN) LIKE '%airbus%' THEN 'Airbus'
                    WHEN LOWER(l.NAME_EN) LIKE '%boeing%' THEN 'Boeing'
                    WHEN LOWER(l.NAME_EN) LIKE '%embraer%' THEN 'Embraer'
                    WHEN LOWER(l.NAME_EN) LIKE '%atr%' THEN 'ATR'
                    WHEN LOWER(l.NAME_EN) LIKE '%bombardier%' THEN 'Bombardier'
                    ELSE 'Other'
                END,
              'model',a.CODE,'notes',a.NOTE,
              'created',DATE_FORMAT(a.CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(a.UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM airplane a JOIN lkp l ON l.ID=a.MANUFACTURER_ID ORDER BY a.ID;
        """,
        "tools": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM tool ORDER BY ID;
        """,
        "tool_equipments": """
            SELECT JSON_OBJECT('id',ID,'tool_id',TOOL_ID,'factory_id',FACTORY_ID,
              'serial_id',SERIAL_ID,'calibration_date',IFNULL(DATE_FORMAT(CALIBRATION_DATE,'%Y-%m-%d'),NULL))
            FROM tool_equipment ORDER BY ID;
        """,
        "materials": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM material ORDER BY ID;
        """,
        "general_supports": """
            SELECT JSON_OBJECT('id',ID,'name',NAME_EN,'description',NOTE,
              'created',DATE_FORMAT(CREATION_DATE,'%Y-%m-%dT%H:%i:%s'),
              'updated',IFNULL(DATE_FORMAT(UPDATE_DATE,'%Y-%m-%dT%H:%i:%s'),NULL))
            FROM general_support ORDER BY ID;
        """,
    }
    return {name: mysql_json(args, query) for name, query in queries.items()}


def generate(data: dict[str, list[dict[str, Any]]], *, apply_default: bool = False) -> str:
    source_customers = data["customers"]
    source_staff = data["staff"]

    used_country_ids = {int(row["country_id"]) for row in source_customers}
    missing_country_mappings = sorted(used_country_ids - COUNTRY_ISO_BY_LEGACY_ID.keys())
    if missing_country_mappings:
        raise ValueError(f"Missing country mappings: {missing_country_mappings}")

    source_station_ids = {int(row["id"]) for row in data["stations"]}
    if source_station_ids != STATION_IMPORT_MAPPING.keys():
        raise ValueError("Station mapping does not exactly match the source station ids")

    normalized_iatas = [normalized(row["iata"]).upper() for row in source_customers]
    iata_counts = collections.Counter(normalized_iatas)
    source_problem_customer_ids = {
        int(row["id"])
        for row in source_customers
        if not is_valid_iata(normalized(row["iata"]).upper())
        or not is_valid_icao(normalized(row["icao"]).upper() if normalized(row["icao"]) else None)
        or iata_counts[normalized(row["iata"]).upper()] > 1
    }
    approved_problem_customer_ids = set(CUSTOMER_CODE_OVERRIDES) | APPROVED_DUPLICATE_IATA_CUSTOMER_IDS
    unknown_approved_customer_ids = approved_problem_customer_ids - source_problem_customer_ids
    if unknown_approved_customer_ids:
        raise ValueError(f"Approved customer ids are no longer source code problems: {sorted(unknown_approved_customer_ids)}")

    source_customer_ids = {int(row["id"]) for row in source_customers}
    unknown_approved_excluded_customer_ids = APPROVED_EXCLUDED_CUSTOMER_IDS.keys() - source_customer_ids
    if unknown_approved_excluded_customer_ids:
        raise ValueError(f"Approved excluded customer ids are missing from source: {sorted(unknown_approved_excluded_customer_ids)}")

    excluded_customer_ids = (source_problem_customer_ids - approved_problem_customer_ids) | set(APPROVED_EXCLUDED_CUSTOMER_IDS)
    excluded_customers = [row for row in source_customers if int(row["id"]) in excluded_customer_ids]
    customers = [row for row in source_customers if int(row["id"]) not in excluded_customer_ids]
    imported_customer_ids = {int(row["id"]) for row in customers}

    def customer_codes(row: dict[str, Any]) -> tuple[str | None, str | None]:
        source_id = int(row["id"])
        if source_id in CUSTOMER_CODE_OVERRIDES:
            return CUSTOMER_CODE_OVERRIDES[source_id]
        iata = normalized(row["iata"])
        icao = normalized(row["icao"])
        return iata.upper() if iata else None, icao.upper() if icao else None

    email_counts = collections.Counter(normalized(row["email"]).lower() for row in source_staff)
    duplicate_staff = [row for row in source_staff if email_counts[normalized(row["email"]).lower()] > 1]
    duplicate_staff_ids = {int(row["id"]) for row in duplicate_staff}
    unknown_approved_staff_ids = APPROVED_DUPLICATE_STAFF_IDS - duplicate_staff_ids
    if unknown_approved_staff_ids:
        raise ValueError(f"Approved staff ids are no longer duplicate-email records: {sorted(unknown_approved_staff_ids)}")

    duplicate_email_groups = {
        normalized(row["email"]).lower()
        for row in duplicate_staff
    }
    for email in duplicate_email_groups:
        selected = [
            int(row["id"]) for row in duplicate_staff
            if normalized(row["email"]).lower() == email and int(row["id"]) in APPROVED_DUPLICATE_STAFF_IDS
        ]
        if len(selected) != 1:
            raise ValueError(f"Exactly one approved staff record is required for duplicate email {email}: {selected}")

    excluded_staff_ids = duplicate_staff_ids - APPROVED_DUPLICATE_STAFF_IDS
    excluded_staff = [row for row in source_staff if int(row["id"]) in excluded_staff_ids]
    staff = [row for row in source_staff if int(row["id"]) not in excluded_staff_ids]
    imported_staff_ids = {int(row["id"]) for row in staff}

    long_addresses = [row for row in customers if len(str(row["address"] or "")) > 200]
    unknown_address_overrides = set(CUSTOMER_ADDRESS_OVERRIDES) - {int(row["id"]) for row in long_addresses}
    if unknown_address_overrides:
        raise ValueError(f"Address overrides are no longer for long source addresses: {sorted(unknown_address_overrides)}")

    customer_licenses = [
        row for row in data["customer_licenses"] if int(row["customer_id"]) in imported_customer_ids
    ]
    excluded_customer_licenses = [
        row for row in data["customer_licenses"] if int(row["customer_id"]) not in imported_customer_ids
    ]
    staff_licenses = [
        row for row in data["staff_licenses"] if int(row["user_id"]) in imported_staff_ids
    ]
    excluded_staff_licenses = [
        row for row in data["staff_licenses"] if int(row["user_id"]) not in imported_staff_ids
    ]

    source_services = data["services"]
    missing_retired_service_ids = RETIRED_LEGACY_SERVICE_IDS - {
        int(row["id"]) for row in source_services
    }
    if missing_retired_service_ids:
        raise ValueError(
            "Retired service ids are missing from the legacy source: "
            f"{sorted(missing_retired_service_ids)}"
        )

    retired_services = [
        row for row in source_services if int(row["id"]) in RETIRED_LEGACY_SERVICE_IDS
    ]
    services = [
        row for row in source_services if int(row["id"]) not in RETIRED_LEGACY_SERVICE_IDS
    ]
    operation_types = data["operation_types"]
    aircraft_types = data["aircraft_types"]
    tools = data["tools"]
    tool_ids = {int(row["id"]) for row in tools}
    tool_equipments = [row for row in data["tool_equipments"] if int(row["tool_id"]) in tool_ids]
    materials = data["materials"]
    general_supports = data["general_supports"]

    catalog_name_checks = [
        ("services", services, "name"),
        ("operation types", operation_types, "name"),
        ("tools", tools, "name"),
        ("materials", materials, "name"),
        ("general supports", general_supports, "name"),
    ]
    for label, rows, field in catalog_name_checks:
        counts = collections.Counter(normalized(row[field]) for row in rows)
        duplicates = sorted(name for name, count in counts.items() if name is not None and count > 1)
        if duplicates:
            raise ValueError(f"Duplicate {label} names in legacy source: {duplicates}")

    aircraft_counts = collections.Counter((normalized(row["manufacturer"]), normalized(row["model"]).upper() if normalized(row["model"]) else None) for row in aircraft_types)
    duplicate_aircraft = sorted(key for key, count in aircraft_counts.items() if count > 1)
    if duplicate_aircraft:
        raise ValueError(f"Duplicate aircraft manufacturer/model pairs in legacy source: {duplicate_aircraft}")

    lines: list[str] = []
    add = lines.append
    add("/*")
    add("  NAGS OperationsSystemV3 legacy release-data import — REVIEW DRAFT")
    add("  Generated from Dump20260628.sql via an isolated MySQL restore.")
    add("")
    add("  SAFETY:")
    if apply_default:
        add("    * @Apply defaults to 1, so a valid run commits the seed data.")
        add("    * Run this only against the target SQL Server database after reviewing the draft file.")
        add("    * This direct seeder applies the small schema-prep changes required by the import.")
    else:
        add("    * @Apply defaults to 0, so a fully valid dry run rolls back.")
    add("    * Known unresolved data causes THROW before any DELETE runs.")
    add("    * Stop the API before an eventual real run.")
    add("    * No identity.users rows are inserted. Operational employees become masterdata.staff_members.")
    add("    * Customer IATA is optional/non-unique; customer ICAO remains optional/unique.")
    add(f"    * {len(excluded_customers)} source customer rows were explicitly rejected after review.")
    add(f"    * {len(excluded_staff)} duplicate-email source employees were explicitly rejected; one selected employee per email remains.")
    add(f"    * {len(retired_services)} retired On Call service row was intentionally excluded; On Call is derived from performed work-order service lines.")
    add(f"    * Catalog data included: {len(services)} services, {len(operation_types)} operation types, {len(aircraft_types)} aircraft types, {len(tools)} tools, {len(tool_equipments)} tool equipment rows, {len(materials)} materials, {len(general_supports)} general supports.")
    add("    * Legacy customer-license links are intentionally skipped because the current schema has no target table.")
    add("    * Legacy catalog prices, units, duration rules, package/time fields and aircraft-service price links are intentionally skipped.")
    add("")
    add("  INTENTIONALLY OMITTED FIELDS (approved/outside the implemented model):")
    add("    * Arabic text; manpower prices, time and package fields.")
    add("    * Catalog prices, units, duration rules, package/time fields and aircraft-service price links.")
    add("    * Other source-only columns remain called out in the review output; no extra legacy tables are restored.")
    add("*/")
    add("SET NOCOUNT ON;")
    add("SET XACT_ABORT ON;")
    add("SET ANSI_NULLS ON;")
    add("SET ANSI_PADDING ON;")
    add("SET ANSI_WARNINGS ON;")
    add("SET ARITHABORT ON;")
    add("SET CONCAT_NULL_YIELDS_NULL ON;")
    add("SET QUOTED_IDENTIFIER ON;")
    add("SET NUMERIC_ROUNDABORT OFF;")
    apply_value = 1 if apply_default else 0
    apply_comment = "DIRECT SEED DEFAULT. Change to 0 for a dry run." if apply_default else "REVIEW DEFAULT. Change to 1 only after every blocker is resolved."
    add(f"DECLARE @Apply bit = {apply_value}; -- {apply_comment}\n")
    add("BEGIN TRY")
    add("    BEGIN TRANSACTION;\n")

    if apply_default:
        add("    /* Direct-run schema prep matching the EF migrations required by this legacy import. */")
        add("    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customer_addresses') AND name=N'Line1' AND is_nullable=0)")
        add("        ALTER TABLE masterdata.customer_addresses ALTER COLUMN Line1 nvarchar(200) NULL;")
        add("    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customer_addresses') AND name=N'City' AND is_nullable=0)")
        add("        ALTER TABLE masterdata.customer_addresses ALTER COLUMN City nvarchar(100) NULL;")
        add("    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.stations') AND name=N'City' AND is_nullable=0)")
        add("        ALTER TABLE masterdata.stations ALTER COLUMN City nvarchar(100) NULL;")
        add("    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.customers') AND name=N'IX_customers_IataCode' AND is_unique=1)")
        add("        DROP INDEX IX_customers_IataCode ON masterdata.customers;")
        add("    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customers') AND name=N'IataCode' AND is_nullable=0)")
        add("        ALTER TABLE masterdata.customers ALTER COLUMN IataCode nvarchar(2) NULL;")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.customers') AND name=N'IX_customers_IataCode')")
        add("        CREATE INDEX IX_customers_IataCode ON masterdata.customers(IataCode);")
        add("    IF OBJECT_ID(N'masterdata.__EFMigrationsHistory') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM masterdata.__EFMigrationsHistory WHERE MigrationId=N'20260628220103_MasterData_NullableAddressAndStationCity')")
        add("        INSERT INTO masterdata.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20260628220103_MasterData_NullableAddressAndStationCity', N'10.0.9');")
        add("    IF OBJECT_ID(N'masterdata.__EFMigrationsHistory') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM masterdata.__EFMigrationsHistory WHERE MigrationId=N'20260628223103_MasterData_OptionalNonUniqueCustomerIata')")
        add("        INSERT INTO masterdata.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20260628223103_MasterData_OptionalNonUniqueCustomerIata', N'10.0.9');\n")
        add("    IF OBJECT_ID(N'masterdata.services') IS NULL")
        add("        CREATE TABLE masterdata.services (Id uniqueidentifier NOT NULL CONSTRAINT PK_services PRIMARY KEY, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF OBJECT_ID(N'masterdata.operation_types') IS NULL")
        add("        CREATE TABLE masterdata.operation_types (Id uniqueidentifier NOT NULL CONSTRAINT PK_operation_types PRIMARY KEY, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF OBJECT_ID(N'masterdata.aircraft_types') IS NULL")
        add("        CREATE TABLE masterdata.aircraft_types (Id uniqueidentifier NOT NULL CONSTRAINT PK_aircraft_types PRIMARY KEY, Manufacturer nvarchar(20) NOT NULL, Model nvarchar(50) NOT NULL, Notes nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF OBJECT_ID(N'masterdata.tools') IS NULL")
        add("        CREATE TABLE masterdata.tools (Id uniqueidentifier NOT NULL CONSTRAINT PK_tools PRIMARY KEY, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF OBJECT_ID(N'masterdata.tool_equipments') IS NULL")
        add("        CREATE TABLE masterdata.tool_equipments (Id uniqueidentifier NOT NULL CONSTRAINT PK_tool_equipments PRIMARY KEY, ToolId uniqueidentifier NOT NULL, FactoryId nvarchar(100) NOT NULL, SerialId nvarchar(100) NOT NULL, CalibrationDate date NULL, CONSTRAINT FK_tool_equipments_tools_ToolId FOREIGN KEY (ToolId) REFERENCES masterdata.tools(Id) ON DELETE CASCADE);")
        add("    IF OBJECT_ID(N'masterdata.materials') IS NULL")
        add("        CREATE TABLE masterdata.materials (Id uniqueidentifier NOT NULL CONSTRAINT PK_materials PRIMARY KEY, Name nvarchar(200) NOT NULL, Description nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF OBJECT_ID(N'masterdata.general_supports') IS NULL")
        add("        CREATE TABLE masterdata.general_supports (Id uniqueidentifier NOT NULL CONSTRAINT PK_general_supports PRIMARY KEY, Name nvarchar(200) NOT NULL, Description nvarchar(500) NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL, RowVersion rowversion NOT NULL);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.services') AND name=N'IX_services_Name') CREATE UNIQUE INDEX IX_services_Name ON masterdata.services(Name);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.operation_types') AND name=N'IX_operation_types_Name') CREATE UNIQUE INDEX IX_operation_types_Name ON masterdata.operation_types(Name);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.aircraft_types') AND name=N'IX_aircraft_types_Manufacturer_Model') CREATE UNIQUE INDEX IX_aircraft_types_Manufacturer_Model ON masterdata.aircraft_types(Manufacturer, Model);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.tools') AND name=N'IX_tools_Name') CREATE UNIQUE INDEX IX_tools_Name ON masterdata.tools(Name);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.tool_equipments') AND name=N'IX_tool_equipments_ToolId') CREATE INDEX IX_tool_equipments_ToolId ON masterdata.tool_equipments(ToolId);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.tool_equipments') AND name=N'IX_tool_equipments_ToolId_FactoryId_SerialId') CREATE UNIQUE INDEX IX_tool_equipments_ToolId_FactoryId_SerialId ON masterdata.tool_equipments(ToolId, FactoryId, SerialId);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.materials') AND name=N'IX_materials_Name') CREATE UNIQUE INDEX IX_materials_Name ON masterdata.materials(Name);")
        add("    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.general_supports') AND name=N'IX_general_supports_Name') CREATE UNIQUE INDEX IX_general_supports_Name ON masterdata.general_supports(Name);")
        add("    IF OBJECT_ID(N'masterdata.__EFMigrationsHistory') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM masterdata.__EFMigrationsHistory WHERE MigrationId=N'20260629181346_MasterData_Catalogs')")
        add("        INSERT INTO masterdata.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20260629181346_MasterData_Catalogs', N'10.0.9');\n")

    add("    CREATE TABLE #LegacyLicenses (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Code nvarchar(10) NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyManpower (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyStations (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, SourceName nvarchar(150) NOT NULL, IataCode nvarchar(10) NULL, IcaoCode nvarchar(10) NULL, City nvarchar(100) NULL, CountryIso char(2) NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyCustomers (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(200) NOT NULL, CountryIso char(2) NOT NULL, RawIata nvarchar(10) NULL, RawIcao nvarchar(10) NULL, ImportIata nvarchar(10) NULL, ImportIcao nvarchar(10) NULL, OfficialEmail nvarchar(256) NULL, OfficialPhone nvarchar(30) NULL, AddressLine1 nvarchar(max) NULL, AddressLine2 nvarchar(200) NULL, City nvarchar(100) NULL, PostalCode nvarchar(20) NULL, ContactId uniqueidentifier NOT NULL, ContactName nvarchar(150) NOT NULL, ContactEmail nvarchar(256) NOT NULL, ContactPhone nvarchar(30) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyStaff (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, EmployeeId nvarchar(50) NOT NULL, FullName nvarchar(200) NOT NULL, ImportEmail nvarchar(256) NOT NULL, SourceStationId int NOT NULL, SourceManpowerId int NOT NULL, IsActive bit NOT NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyStaffLicenses (TargetId uniqueidentifier NOT NULL, SourceStaffId int NOT NULL, SourceLicenseId int NOT NULL, LicenseNumber nvarchar(100) NOT NULL);")
    add("    CREATE TABLE #UnmappedCustomerLicenses (SourceCustomerId int NOT NULL, SourceLicenseId int NOT NULL);")
    add("    CREATE TABLE #ExcludedSourceCustomers (SourceId int PRIMARY KEY, Name nvarchar(200) NOT NULL, RawIata nvarchar(10) NULL, RawIcao nvarchar(10) NULL, Reason nvarchar(200) NOT NULL);")
    add("    CREATE TABLE #ExcludedSourceStaff (SourceId int PRIMARY KEY, EmployeeId nvarchar(50) NOT NULL, FullName nvarchar(200) NOT NULL, Email nvarchar(256) NOT NULL, Reason nvarchar(200) NOT NULL);")
    add("    CREATE TABLE #ExcludedCustomerLicenseRelations (SourceCustomerId int NOT NULL, SourceLicenseId int NOT NULL);")
    add("    CREATE TABLE #ExcludedStaffLicenseRelations (SourceStaffId int NOT NULL, SourceLicenseId int NOT NULL);")
    add("    CREATE TABLE #IdentityOnlySourceUsers (SourceId int PRIMARY KEY, EmployeeId nvarchar(50) NOT NULL, FullName nvarchar(200) NOT NULL, Email nvarchar(256) NOT NULL, SourceStationId int NOT NULL, IsActive bit NOT NULL);")
    add("    CREATE TABLE #LegacyServices (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyOperationTypes (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyAircraftTypes (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Manufacturer nvarchar(20) NOT NULL, Model nvarchar(50) NOT NULL, Notes nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyTools (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyToolEquipments (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, SourceToolId int NOT NULL, FactoryId nvarchar(100) NOT NULL, SerialId nvarchar(100) NOT NULL, CalibrationDate date NULL);")
    add("    CREATE TABLE #LegacyMaterials (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(200) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);")
    add("    CREATE TABLE #LegacyGeneralSupports (SourceId int PRIMARY KEY, TargetId uniqueidentifier NOT NULL, Name nvarchar(200) NOT NULL, Description nvarchar(500) NULL, CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NULL);\n")

    add(insert_values("#LegacyLicenses", ["SourceId", "TargetId", "Code", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("license", r["id"]), normalized(r["code"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in data["licenses"]
    ]).rstrip())
    add(insert_values("#LegacyManpower", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("manpower", r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in data["manpower"]
    ]).rstrip())
    add(insert_values("#LegacyStations", ["SourceId", "TargetId", "SourceName", "IataCode", "IcaoCode", "City", "CountryIso", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("station", r["id"]), normalized(r["name"]), *STATION_IMPORT_MAPPING[int(r["id"])], utc(r["created"]), utc(r["updated"]))
        for r in data["stations"]
    ]).rstrip())
    add(insert_values("#LegacyCustomers", ["SourceId", "TargetId", "Name", "CountryIso", "RawIata", "RawIcao", "ImportIata", "ImportIcao", "OfficialEmail", "OfficialPhone", "AddressLine1", "AddressLine2", "City", "PostalCode", "ContactId", "ContactName", "ContactEmail", "ContactPhone", "CreatedAtUtc", "UpdatedAtUtc"], [
        (
            int(r["id"]), dotnet_guid("customer", r["id"]), normalized(r["name"]), COUNTRY_ISO_BY_LEGACY_ID[int(r["country_id"])],
            normalized(r["iata"]), normalized(r["icao"]), *customer_codes(r),
            normalized(r["email"]).lower() if normalized(r["email"]) else None, normalized(r["phone"]), normalized(r["address"]), normalized(r["pobox"]),
            normalized(r["city"]), normalized(r["postal"]), dotnet_guid("customer-contact", r["id"]), normalized(r["contact_name"]),
            normalized(r["contact_email"]).lower(), normalized(r["contact_phone"]), utc(r["created"]), utc(r["updated"]),
        )
        for r in customers
    ]).rstrip())
    add(insert_values("#LegacyStaff", ["SourceId", "TargetId", "EmployeeId", "FullName", "ImportEmail", "SourceStationId", "SourceManpowerId", "IsActive", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("staff", r["id"]), normalized(r["employee_id"]).upper(), normalized(r["name"]), normalized(r["email"]).lower(), int(r["station_id"]), int(r["manpower_id"]), int(r["status_id"]) == 10, utc(r["created"]), utc(r["updated"]))
        for r in staff
    ]).rstrip())
    add(insert_values("#LegacyStaffLicenses", ["TargetId", "SourceStaffId", "SourceLicenseId", "LicenseNumber"], [
        (dotnet_guid("staff-license", f"{r['user_id']}:{r['license_id']}"), int(r["user_id"]), int(r["license_id"]), normalized(r["license_number"]).upper())
        for r in staff_licenses
    ]).rstrip())
    add(insert_values("#UnmappedCustomerLicenses", ["SourceCustomerId", "SourceLicenseId"], [
        (int(r["customer_id"]), int(r["license_id"])) for r in customer_licenses
    ]).rstrip())
    add(insert_values("#ExcludedSourceCustomers", ["SourceId", "Name", "RawIata", "RawIcao", "Reason"], [
        (int(r["id"]), normalized(r["name"]), normalized(r["iata"]), normalized(r["icao"]),
         APPROVED_EXCLUDED_CUSTOMER_IDS.get(int(r["id"]), "Rejected after invalid-code review: source row is not a customer"))
        for r in excluded_customers
    ]).rstrip())
    add(insert_values("#ExcludedSourceStaff", ["SourceId", "EmployeeId", "FullName", "Email", "Reason"], [
        (int(r["id"]), normalized(r["employee_id"]).upper(), normalized(r["name"]), normalized(r["email"]).lower(), "Rejected after duplicate-email review: another employee was selected")
        for r in excluded_staff
    ]).rstrip())
    add(insert_values("#ExcludedCustomerLicenseRelations", ["SourceCustomerId", "SourceLicenseId"], [
        (int(r["customer_id"]), int(r["license_id"])) for r in excluded_customer_licenses
    ]).rstrip())
    add(insert_values("#ExcludedStaffLicenseRelations", ["SourceStaffId", "SourceLicenseId"], [
        (int(r["user_id"]), int(r["license_id"])) for r in excluded_staff_licenses
    ]).rstrip())
    add(insert_values("#IdentityOnlySourceUsers", ["SourceId", "EmployeeId", "FullName", "Email", "SourceStationId", "IsActive"], [
        (int(r["id"]), normalized(r["employee_id"]).upper(), normalized(r["name"]), normalized(r["email"]).lower(), int(r["station_id"]), int(r["status_id"]) == 10)
        for r in data["user_only"]
    ]).rstrip())
    add(insert_values("#LegacyServices", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), service_guid(r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in services
    ]).rstrip())
    add(insert_values("#LegacyOperationTypes", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), operation_type_guid(r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in operation_types
    ]).rstrip())
    add(insert_values("#LegacyAircraftTypes", ["SourceId", "TargetId", "Manufacturer", "Model", "Notes", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("aircraft-type", r["id"]), normalized(r["manufacturer"]), normalized(r["model"]).upper(), normalized(r["notes"]), utc(r["created"]), utc(r["updated"]))
        for r in aircraft_types
    ]).rstrip())
    add(insert_values("#LegacyTools", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("tool", r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in tools
    ]).rstrip())
    add(insert_values("#LegacyToolEquipments", ["SourceId", "TargetId", "SourceToolId", "FactoryId", "SerialId", "CalibrationDate"], [
        (
            int(r["id"]), dotnet_guid("tool-equipment", r["id"]), int(r["tool_id"]),
            normalized(r["factory_id"]), normalized(r["serial_id"]),
            normalized(r["calibration_date"])[:10] if normalized(r["calibration_date"]) else None,
        )
        for r in tool_equipments
    ]).rstrip())
    add(insert_values("#LegacyMaterials", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("material", r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in materials
    ]).rstrip())
    add(insert_values("#LegacyGeneralSupports", ["SourceId", "TargetId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc"], [
        (int(r["id"]), dotnet_guid("general-support", r["id"]), normalized(r["name"]), normalized(r["description"]), utc(r["created"]), utc(r["updated"]))
        for r in general_supports
    ]).rstrip())

    add("\n    /* APPROVED: corrected customer codes. Null IATA means the customer has no IATA code. */")
    add("    CREATE TABLE #CustomerCodeDecisions (SourceCustomerId int PRIMARY KEY, IataCode nvarchar(10) NULL, IcaoCode nvarchar(10) NULL);")
    add(insert_values("#CustomerCodeDecisions", ["SourceCustomerId", "IataCode", "IcaoCode"], [
        (source_id, codes[0], codes[1]) for source_id, codes in sorted(CUSTOMER_CODE_OVERRIDES.items())
    ]).rstrip())
    add("    UPDATE c SET ImportIata=x.IataCode, ImportIcao=x.IcaoCode FROM #LegacyCustomers c JOIN #CustomerCodeDecisions x ON x.SourceCustomerId=c.SourceId;\n")

    add("    /* ADDRESS REVIEW: both 250-character source addresses use approved shorter replacements. */")
    add("    CREATE TABLE #CustomerAddressCorrections (SourceCustomerId int PRIMARY KEY, AddressLine1 nvarchar(max) NULL);")
    add(insert_values("#CustomerAddressCorrections", ["SourceCustomerId", "AddressLine1"], [
        (int(r["id"]), CUSTOMER_ADDRESS_OVERRIDES.get(int(r["id"]), normalized(r["address"]))) for r in long_addresses
    ]).rstrip())
    add("    UPDATE c SET AddressLine1=x.AddressLine1 FROM #LegacyCustomers c JOIN #CustomerAddressCorrections x ON x.SourceCustomerId=c.SourceCustomerId;\n".replace("c.SourceCustomerId", "c.SourceId"))

    add("    /* APPROVED: one retained operational employee for each duplicate email. */")
    add("    CREATE TABLE #SelectedDuplicateEmailStaff (SourceStaffId int PRIMARY KEY, Email nvarchar(256) NOT NULL);")
    add(insert_values("#SelectedDuplicateEmailStaff", ["SourceStaffId", "Email"], [
        (int(r["id"]), normalized(r["email"]).lower())
        for r in source_staff if int(r["id"]) in APPROVED_DUPLICATE_STAFF_IDS
    ]).rstrip())

    add("    /* Read-only review result sets. */")
    add("    SELECT 'Invalid imported customer code' AS Issue, SourceId, Name, RawIata, RawIcao, ImportIata, ImportIcao FROM #LegacyCustomers WHERE (ImportIata IS NOT NULL AND (LEN(ImportIata)<>2 OR ImportIata COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%')) OR (ImportIcao IS NOT NULL AND (LEN(ImportIcao)<>3 OR ImportIcao COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z]%')) ORDER BY SourceId;")
    add("    SELECT 'Approved duplicate customer IATA' AS Issue, SourceId, Name, ImportIata, ImportIcao FROM #LegacyCustomers WHERE ImportIata IN (SELECT ImportIata FROM #LegacyCustomers WHERE ImportIata IS NOT NULL GROUP BY ImportIata HAVING COUNT(*)>1) ORDER BY ImportIata,SourceId;")
    add("    SELECT 'Unresolved duplicate customer ICAO' AS Issue, SourceId, Name, ImportIata, ImportIcao FROM #LegacyCustomers WHERE ImportIcao IN (SELECT ImportIcao FROM #LegacyCustomers WHERE ImportIcao IS NOT NULL GROUP BY ImportIcao HAVING COUNT(*)>1) ORDER BY ImportIcao,SourceId;")
    add("    SELECT 'Rejected source row (not a customer)' AS Issue, * FROM #ExcludedSourceCustomers ORDER BY SourceId;")
    add("    SELECT 'Address exceeds 200' AS Issue, SourceId, Name, LEN(AddressLine1) AS AddressLength, AddressLine1 FROM #LegacyCustomers WHERE LEN(AddressLine1)>200 ORDER BY SourceId;")
    add("    SELECT 'Selected duplicate-email employee' AS Issue, s.SourceId, s.EmployeeId, s.FullName, s.ImportEmail, st.SourceName AS Station, m.Name AS ManpowerType FROM #LegacyStaff s JOIN #SelectedDuplicateEmailStaff x ON x.SourceStaffId=s.SourceId JOIN #LegacyStations st ON st.SourceId=s.SourceStationId JOIN #LegacyManpower m ON m.SourceId=s.SourceManpowerId ORDER BY s.ImportEmail,s.SourceId;")
    add("    SELECT 'Rejected duplicate-email employee' AS Issue, * FROM #ExcludedSourceStaff ORDER BY Email,SourceId;")
    add("    SELECT 'Incomplete station mapping' AS Issue, SourceId, SourceName, IataCode, IcaoCode, City, CountryIso FROM #LegacyStations WHERE IataCode IS NULL OR LEN(IataCode)<>3 ORDER BY SourceId;")
    add("    SELECT 'Invalid email preserved from source' AS Issue, SourceId, Name, OfficialEmail, ContactEmail FROM #LegacyCustomers WHERE (OfficialEmail IS NOT NULL AND OfficialEmail NOT LIKE '%_@_%._%') OR ContactEmail NOT LIKE '%_@_%._%' ORDER BY SourceId;")
    add("    SELECT 'Identity-only source row (not imported)' AS Issue, * FROM #IdentityOnlySourceUsers ORDER BY SourceId;")
    add("    SELECT 'Skipped customer-license relation (no target model)' AS Issue, COUNT(*) AS RelationshipCount FROM #UnmappedCustomerLicenses;\n")
    add("    SELECT 'Relation omitted with rejected customer' AS Issue, COUNT(*) AS RelationshipCount FROM #ExcludedCustomerLicenseRelations;")
    add("    SELECT 'Staff-license relation omitted with rejected employee' AS Issue, COUNT(*) AS RelationshipCount FROM #ExcludedStaffLicenseRelations;\n")
    add("    SELECT 'Skipped catalog legacy fields' AS Issue, N'prices, units, duration rules, package/time fields, product/system ids and aircraft-service price links are outside the current schema' AS Details;\n")

    add("    /* Hard safety gates: every retained record is imported or the batch stops before deletion. */")
    add("    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customer_addresses') AND name=N'Line1' AND is_nullable=1) THROW 50001, 'Apply the nullable-address EF migration before importing.', 1;")
    add("    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customer_addresses') AND name=N'City' AND is_nullable=1) THROW 50002, 'Apply the nullable-address EF migration before importing.', 1;")
    add("    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.stations') AND name=N'City' AND is_nullable=1) THROW 50003, 'Apply the nullable-station-city EF migration before importing.', 1;")
    add("    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'masterdata.customers') AND name=N'IataCode' AND is_nullable=1) OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'masterdata.customers') AND name=N'IX_customers_IataCode' AND is_unique=0) THROW 50004, 'Apply the optional/non-unique customer-IATA EF migration before importing.', 1;")
    add("    IF EXISTS (SELECT 1 FROM #LegacyCustomers WHERE (ImportIata IS NOT NULL AND (LEN(ImportIata)<>2 OR ImportIata COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%')) OR (ImportIcao IS NOT NULL AND (LEN(ImportIcao)<>3 OR ImportIcao COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z]%'))) THROW 50005, 'An imported customer still has an invalid IATA/ICAO code.', 1;")
    add("    IF EXISTS (SELECT ImportIcao FROM #LegacyCustomers WHERE ImportIcao IS NOT NULL GROUP BY ImportIcao HAVING COUNT(*)>1) THROW 50006, 'Resolve duplicate normalized customer ICAO codes.', 1;")
    add("    IF EXISTS (SELECT 1 FROM #LegacyCustomers WHERE LEN(AddressLine1)>200) THROW 50007, 'A customer address still exceeds the target 200-character limit.', 1;")
    add("    IF EXISTS (SELECT ImportEmail FROM #LegacyStaff GROUP BY ImportEmail HAVING COUNT(*)>1) THROW 50008, 'Duplicate station-staff emails remain after the approved selections.', 1;")
    add("    IF EXISTS (SELECT 1 FROM #LegacyStations WHERE IataCode IS NULL OR LEN(IataCode)<>3) THROW 50009, 'Complete the station IATA mapping.', 1;")
    add(f"    IF (SELECT COUNT(*) FROM #LegacyCustomers)<>{len(customers)} OR (SELECT COUNT(*) FROM #ExcludedSourceCustomers)<>{len(excluded_customers)} THROW 50010, 'The reviewed customer keep/reject counts changed.', 1;")
    add(f"    IF (SELECT COUNT(*) FROM #LegacyStaff)<>{len(staff)} OR (SELECT COUNT(*) FROM #ExcludedSourceStaff)<>{len(excluded_staff)} THROW 50011, 'The reviewed employee keep/reject counts changed.', 1;")
    add("    IF OBJECT_ID(N'masterdata.services') IS NULL OR OBJECT_ID(N'masterdata.operation_types') IS NULL OR OBJECT_ID(N'masterdata.aircraft_types') IS NULL OR OBJECT_ID(N'masterdata.tools') IS NULL OR OBJECT_ID(N'masterdata.tool_equipments') IS NULL OR OBJECT_ID(N'masterdata.materials') IS NULL OR OBJECT_ID(N'masterdata.general_supports') IS NULL THROW 50012, 'Apply the MasterData_Catalogs migration before importing.', 1;")
    add("    IF EXISTS (SELECT 1 FROM (SELECT CountryIso FROM #LegacyCustomers UNION SELECT CountryIso FROM #LegacyStations) source_codes LEFT JOIN masterdata.countries c ON c.IsoCode=source_codes.CountryIso WHERE c.Id IS NULL) THROW 50013, 'A required seeded country is missing.', 1;")
    add(f"    IF (SELECT COUNT(*) FROM #LegacyServices)<>{len(services)} OR (SELECT COUNT(*) FROM #LegacyOperationTypes)<>{len(operation_types)} OR (SELECT COUNT(*) FROM #LegacyAircraftTypes)<>{len(aircraft_types)} OR (SELECT COUNT(*) FROM #LegacyTools)<>{len(tools)} OR (SELECT COUNT(*) FROM #LegacyToolEquipments)<>{len(tool_equipments)} OR (SELECT COUNT(*) FROM #LegacyMaterials)<>{len(materials)} OR (SELECT COUNT(*) FROM #LegacyGeneralSupports)<>{len(general_supports)} THROW 50014, 'The reviewed catalog row counts changed.', 1;\n")

    add("    /* Destructive reset starts only after all gates above pass. Countries and EF histories remain. */")
    add("    DELETE FROM audit.inbox_messages;")
    add("    DELETE FROM audit.outbox_messages;")
    add("    DELETE FROM audit.audit_trails;")
    add("    DELETE FROM [identity].user_sessions;")
    add("    DELETE FROM [identity].inbox_messages;")
    add("    DELETE FROM [identity].outbox_messages;")
    add("    DELETE FROM [identity].users;")
    add("    DELETE FROM [identity].roles;")
    add("    DELETE FROM masterdata.staff_member_licenses;")
    add("    DELETE FROM masterdata.staff_members;")
    add("    DELETE FROM masterdata.customer_contacts;")
    add("    DELETE FROM masterdata.customer_addresses;")
    add("    DELETE FROM masterdata.customers;")
    add("    DELETE FROM masterdata.stations;")
    add("    DELETE FROM masterdata.licenses;")
    add("    DELETE FROM masterdata.manpower_types;")
    add("    DELETE FROM masterdata.tool_equipments;")
    add("    DELETE FROM masterdata.tools;")
    add("    DELETE FROM masterdata.materials;")
    add("    DELETE FROM masterdata.general_supports;")
    add("    DELETE FROM masterdata.aircraft_types;")
    add("    DELETE FROM masterdata.operation_types;")
    add("    DELETE FROM masterdata.services;")
    add("    DELETE FROM masterdata.inbox_messages;")
    add("    DELETE FROM masterdata.outbox_messages;\n")

    add("    INSERT INTO masterdata.services (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyServices;")
    add("    INSERT INTO masterdata.operation_types (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyOperationTypes;")
    add("    INSERT INTO masterdata.aircraft_types (Id,Manufacturer,Model,Notes,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Manufacturer,Model,Notes,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyAircraftTypes;")
    add("    INSERT INTO masterdata.tools (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyTools;")
    add("    INSERT INTO masterdata.tool_equipments (Id,ToolId,FactoryId,SerialId,CalibrationDate) SELECT e.TargetId,t.TargetId,e.FactoryId,e.SerialId,e.CalibrationDate FROM #LegacyToolEquipments e JOIN #LegacyTools t ON t.SourceId=e.SourceToolId;")
    add("    INSERT INTO masterdata.materials (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyMaterials;")
    add("    INSERT INTO masterdata.general_supports (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyGeneralSupports;\n")

    add("    INSERT INTO masterdata.licenses (Id,Code,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Code,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyLicenses;")
    add("    INSERT INTO masterdata.manpower_types (Id,Name,Description,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT TargetId,Name,Description,1,CreatedAtUtc,UpdatedAtUtc FROM #LegacyManpower;")
    add("    INSERT INTO masterdata.stations (Id,IataCode,IcaoCode,Name,City,CountryId,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT s.TargetId,s.IataCode,s.IcaoCode,s.SourceName,s.City,c.Id,1,s.CreatedAtUtc,s.UpdatedAtUtc FROM #LegacyStations s JOIN masterdata.countries c ON c.IsoCode=s.CountryIso;")
    add("    INSERT INTO masterdata.customers (Id,IataCode,IcaoCode,Name,CountryId,OfficialEmail,OfficialPhone,LogoFileReference,IsActive,CreatedAtUtc,UpdatedAtUtc) SELECT x.TargetId,x.ImportIata,x.ImportIcao,x.Name,c.Id,x.OfficialEmail,x.OfficialPhone,NULL,1,x.CreatedAtUtc,x.UpdatedAtUtc FROM #LegacyCustomers x JOIN masterdata.countries c ON c.IsoCode=x.CountryIso;")
    add("    INSERT INTO masterdata.customer_addresses (CustomerId,Line1,Line2,City,Region,PostalCode) SELECT TargetId,AddressLine1,AddressLine2,City,NULL,PostalCode FROM #LegacyCustomers;")
    add("    INSERT INTO masterdata.customer_contacts (Id,CustomerId,Name,JobTitle,Email,Phone,LinkedUserId,IsActive,CreatedAtUtc,UpdatedAtUtc,PortalCorrelationId,PortalFailureReason,PortalState) SELECT ContactId,TargetId,ContactName,NULL,ContactEmail,ContactPhone,NULL,1,CreatedAtUtc,UpdatedAtUtc,NULL,NULL,N'None' FROM #LegacyCustomers;")
    add("    INSERT INTO masterdata.staff_members (Id,FullName,EmployeeId,Email,StationId,ManpowerTypeId,EmploymentStartDate,EmploymentEndDate,WorkingScheduleMask,LinkedUserId,IsActive,CreatedAtUtc,UpdatedAtUtc,PortalCorrelationId,PortalFailureReason,PortalState) SELECT s.TargetId,s.FullName,s.EmployeeId,s.ImportEmail,st.TargetId,m.TargetId,NULL,NULL,NULL,NULL,s.IsActive,s.CreatedAtUtc,s.UpdatedAtUtc,NULL,NULL,N'None' FROM #LegacyStaff s JOIN #LegacyStations st ON st.SourceId=s.SourceStationId JOIN #LegacyManpower m ON m.SourceId=s.SourceManpowerId;")
    add("    INSERT INTO masterdata.staff_member_licenses (Id,StaffMemberId,LicenseId,LicenseNumber) SELECT sl.TargetId,s.TargetId,l.TargetId,sl.LicenseNumber FROM #LegacyStaffLicenses sl JOIN #LegacyStaff s ON s.SourceId=sl.SourceStaffId JOIN #LegacyLicenses l ON l.SourceId=sl.SourceLicenseId;\n")

    add("    SELECT 'countries (preserved)' AS Entity, COUNT(*) AS [RowCount] FROM masterdata.countries UNION ALL")
    add("    SELECT 'licenses',COUNT(*) FROM masterdata.licenses UNION ALL SELECT 'manpower types',COUNT(*) FROM masterdata.manpower_types UNION ALL SELECT 'services',COUNT(*) FROM masterdata.services UNION ALL SELECT 'operation types',COUNT(*) FROM masterdata.operation_types UNION ALL SELECT 'aircraft types',COUNT(*) FROM masterdata.aircraft_types UNION ALL SELECT 'tools',COUNT(*) FROM masterdata.tools UNION ALL SELECT 'tool equipment',COUNT(*) FROM masterdata.tool_equipments UNION ALL SELECT 'materials',COUNT(*) FROM masterdata.materials UNION ALL SELECT 'general supports',COUNT(*) FROM masterdata.general_supports UNION ALL SELECT 'stations',COUNT(*) FROM masterdata.stations UNION ALL SELECT 'customers',COUNT(*) FROM masterdata.customers UNION ALL SELECT 'customer contacts',COUNT(*) FROM masterdata.customer_contacts UNION ALL SELECT 'station staff',COUNT(*) FROM masterdata.staff_members UNION ALL SELECT 'staff licenses',COUNT(*) FROM masterdata.staff_member_licenses UNION ALL SELECT 'identity users',COUNT(*) FROM [identity].users;\n")
    add("    IF @Apply=1")
    add("    BEGIN")
    add("        COMMIT TRANSACTION;")
    add("        PRINT 'IMPORT COMMITTED.';")
    add("    END")
    add("    ELSE")
    add("    BEGIN")
    add("        ROLLBACK TRANSACTION;")
    add("        PRINT 'DRY RUN ONLY: all changes rolled back. Set @Apply=1 only after review.';")
    add("    END")
    add("END TRY")
    add("BEGIN CATCH")
    add("    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;")
    add("    THROW;")
    add("END CATCH;")
    return "\n".join(lines) + "\n"


def main() -> None:
    args = parse_args()
    data = load_source(args)
    output = generate(data, apply_default=args.apply_default)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(output, encoding="utf-8")
    print(f"Generated {args.output} ({len(output):,} characters)")


if __name__ == "__main__":
    main()
