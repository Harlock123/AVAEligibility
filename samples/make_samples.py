#!/usr/bin/env python3
"""Makes AVA Eligibility's samples: an invented family clinic's 270 inquiries for next week's appointments, the 271s
its four payers sent back, and the appointment list. Every person, payer, NPI and number here is made up.

Each patient has one situation planted in the answers (see PATIENTS); the tests pin what each one says. The files are
the same every run (fixed dates and control numbers), so tests can count on them.

    python3 make_samples.py
"""
import csv
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ASKED = "20261125"          # when the clinic asked
ASKED_TIME = "0815"
ANSWERED_TIME = "0816"
CLINIC = "MAPLE STREET FAMILY MEDICINE"
CLINIC_TRN = "9MAPLE0001"   # TRN03: the originator's ID, 1 or 9 then nine digits' worth


def luhn_npi(base9):
    """An NPI: nine digits and a Luhn check digit computed over 80840 + the nine (as CMS describes)."""
    digits = [int(c) for c in "80840" + base9]
    total = 0
    for i, d in enumerate(reversed(digits)):
        if i % 2 == 0:
            d *= 2
            if d > 9:
                d -= 9
        total += d
    return base9 + str((10 - total % 10) % 10)


CLINIC_NPI = luhn_npi("164203187")
DR_PATEL = ("PATEL", "ANJALI", luhn_npi("152907443"))
DR_OKAFOR = ("OKAFOR", "CHIDI", luhn_npi("173300158"))
DR_REYES = ("REYES", "LUCIA", luhn_npi("188812094"))   # a PCP at another practice

PAYERS = {
    "BWHP1": "BLUEWATER HEALTH PLAN",
    "PRMC2": "PRAIRIE MEDICAID CARE",
    "SMHMO": "SUMMIT HMO",
    "CSMUT": "CASCADE MUTUAL",
}

# The appointments: (date, time, last, first, middle, dob, sex, payer, member id as the clinic has it, provider,
# visit, procedure, charge, the trace number of its inquiry, and what's planted).
PATIENTS = [
    ("2026-12-01", "08:30", "HARTWELL", "DANIEL", "M", "19710314", "M", "BWHP1", "BWH400100201", DR_PATEL, "Office visit", "99214", 185.00, "BW0001", "active, full benefits"),
    ("2026-12-01", "09:00", "HARTWELL", "EMMA", "", "20140602", "F", "BWHP1", "BWH400100201", DR_PATEL, "Well child", "99393", 210.00, "BW0002", "dependent on her father's plan"),
    ("2026-12-01", "09:30", "OYELARAN", "GRACE", "", "19880930", "F", "BWHP1", "BWH400100377", DR_OKAFOR, "Office visit", "99213", 140.00, "BW0003", "coverage ended 31 October"),
    ("2026-12-01", "10:00", "KOWALCZYK", "PETER", "J", "19591111", "M", "BWHP1", "BWH40010O512", DR_OKAFOR, "Office visit", "99213", 140.00, "BW0004", "member ID typed with a letter O"),
    ("2026-12-01", "10:30", "BRANNIGAN", "ROSE", "", "19650218", "F", "BWHP1", "BWH400100648", DR_PATEL, "Office visit", "99214", 185.00, "BW0005", "birth date doesn't match (payer has 28 Feb)"),
    ("2026-12-02", "08:30", "DELACROIX", "MARCUS", "", "19570704", "M", "BWHP1", "BWH400100733", DR_PATEL, "Office visit", "99214", 185.00, "BW0006", "Medicare is primary"),
    ("2026-12-02", "09:00", "NAKAMURA", "KENJI", "", "19791219", "M", "BWHP1", "BWH400100815", DR_OKAFOR, "MRI, lumbar spine", "72148", 1450.00, "BW0007", "MRI needs prior authorization"),
    ("2026-12-02", "10:00", "FLORES", "ANA", "", "19960422", "F", "PRMC2", "PMC7700112", DR_OKAFOR, "Therapy, 60 minutes", "90837", 160.00, "PR0001", "Medicaid: $0 copay, 8 of 20 therapy visits left"),
    ("2026-12-02", "10:30", "FLORES", "MATEO", "", "20190815", "M", "PRMC2", "PMC7700113", DR_PATEL, "Office visit", "99213", 140.00, "PR0002", "PCP on file is at another practice"),
    ("2026-12-03", "08:30", "ABERNATHY", "LOUISE", "", "19830109", "F", "SMHMO", "SMH55120", DR_PATEL, "Office visit", "99213", 140.00, "SM0001", "HMO, PCP is Dr Patel: ready"),
    ("2026-12-03", "09:00", "VANTERPOOL", "SIMONE", "", "19900527", "F", "SMHMO", "SMH55988", DR_PATEL, "Office visit", "99213", 140.00, "SM0002", "subscriber not found"),
    ("2026-12-03", "09:30", "ODUYA", "SAMUEL", "", "19751003", "M", "CSMUT", "CMI22001", DR_OKAFOR, "Office visit", "99213", 140.00, "CS0001", "payer didn't answer: try later"),
    ("2026-12-03", "10:00", "PRICE", "NOLAN", "", "19920318", "M", "", "", DR_OKAFOR, "Office visit", "99213", 140.00, "", "self-pay: nothing to ask"),
]


def seg(*elements):
    """A segment, with trailing empty elements left off."""
    parts = list(elements)
    while parts and parts[-1] == "":
        parts.pop()
    return "*".join(str(p) for p in parts) + "~"


def isa(control, functional, sender, receiver):
    return (
        "ISA*00*          *00*          *ZZ*{:<15}*ZZ*{:<15}*{}*{}*^*00501*{:09d}*0*T*:~".format(sender, receiver, ASKED[2:], ASKED_TIME if functional == "HS" else ANSWERED_TIME, control),
        seg("GS", functional, sender, receiver, ASKED, ASKED_TIME if functional == "HS" else ANSWERED_TIME, control, "X", "005010X279A1"),
    )


def wrap(control, functional, sender, receiver, transactions):
    head = isa(control, functional, sender, receiver)
    out = [head[0], head[1]]
    for t in transactions:
        out.extend(t)
    out.append(seg("GE", len(transactions), control))
    out.append(seg("IEA", 1, "{:09d}".format(control)))
    return "\n".join(out) + "\n"


def transaction(code, number, body):
    st = seg("ST", code, "{:04d}".format(number), "005010X279A1")
    return [st] + body + [seg("SE", len(body) + 2, "{:04d}".format(number))]


def d8(date):
    return date.replace("-", "")


# ───────────────────────────── the 270s ─────────────────────────────

def inquiries():
    """One 270 transaction per payer, each asking about that payer's patients for their visit dates."""
    out = []
    number = 0
    for payer_id, payer_name in PAYERS.items():
        people = [p for p in PATIENTS if p[7] == payer_id]
        number += 1
        body = [seg("BHT", "0022", "13", "MSFM{:04d}".format(number), ASKED, ASKED_TIME)]
        body += [seg("HL", 1, "", 20, 1), seg("NM1", "PR", 2, payer_name, "", "", "", "", "PI", payer_id)]
        body += [seg("HL", 2, 1, 21, 1), seg("NM1", "1P", 2, CLINIC, "", "", "", "", "XX", CLINIC_NPI)]
        hl = 2
        i = 0
        while i < len(people):
            p = people[i]
            dependents = [q for q in people[i + 1:] if q[8] == p[8] and q[2] == p[2] and q[7] == p[7]]
            hl += 1
            subscriber_hl = hl
            body.append(seg("HL", hl, 2, 22, 1 if dependents else 0))
            body.append(seg("TRN", 1, p[13], CLINIC_TRN))
            body.append(seg("NM1", "IL", 1, p[2], p[3], p[4], "", "", "MI", p[8]))
            body.append(seg("DMG", "D8", p[5], p[6]))
            body.append(seg("DTP", "291", "D8", d8(p[0])))
            body.append(seg("EQ", "30^98" if p[11] != "72148" else "30^62"))
            # A dependent's visit goes under its own HL, below its subscriber.
            if dependents:
                for q in dependents:
                    hl += 1
                    body.append(seg("HL", hl, subscriber_hl, 23, 0))
                    body.append(seg("TRN", 1, q[13], CLINIC_TRN))
                    body.append(seg("NM1", "03", 1, q[2], q[3], q[4]))
                    body.append(seg("DMG", "D8", q[5], q[6]))
                    body.append(seg("DTP", "291", "D8", d8(q[0])))
                    body.append(seg("EQ", "30^98"))
            i += 1 + len(dependents)
        out.append(transaction("270", number, body))
    return out


# ───────────────────────────── the 271s ─────────────────────────────

def eb(*elements):
    return seg("EB", *elements)


def answer_people(payer_id):
    """The 2000C/2000D loops a payer sends back, by patient (each a list of segments, HL numbering filled later)."""
    out = []
    for p in [p for p in PATIENTS if p[7] == payer_id]:
        date, _, last, first, middle, dob, sex, _, member, provider, _, procedure, _, trace, _ = p
        echo = [seg("TRN", 2, trace, CLINIC_TRN), seg("TRN", 1, "R" + trace, "9" + payer_id + "0001")]
        name = seg("NM1", "IL", 1, last, first, middle, "", "", "MI", member)
        address = [seg("N3", "{} {}".format(100 + PATIENTS.index(p) * 37, ["OAK", "CEDAR", "BIRCH", "ELM", "ASPEN", "LINDEN"][PATIENTS.index(p) % 6] + " ST")),
                   seg("N4", "SPRINGFIELD", "IL", "62704")]
        dmg = seg("DMG", "D8", dob, sex)
        if first == "DANIEL":
            body = echo + [name, seg("REF", "6P", "GRP77015", "MAPLE TOOL AND DIE"), seg("REF", "18", "GOLDPPO1500")] + address + [dmg, seg("INS", "Y", "18"), seg("DTP", "346", "D8", "20260101")]
            body += plan_ppo(dependent=False)
            out.append(("sub", body, [dependent_emma()]))
        elif first == "EMMA":
            continue
        elif first == "GRACE":
            body = echo + [name, seg("REF", "6P", "GRP77015", "MAPLE TOOL AND DIE")] + address + [dmg, seg("DTP", "346", "D8", "20250101"), seg("DTP", "347", "D8", "20261031")]
            body += [eb(6, "IND", 30, "PR", "GOLD PPO 1500"), seg("MSG", "COVERAGE TERMINATED 10/31/2026")]
            out.append(("sub", body, []))
        elif first == "PETER":
            body = echo + [name, seg("AAA", "N", "", 72, "C")]
            out.append(("sub", body, []))
        elif first == "ROSE":
            # The payer's records say 28 February; the clinic sent the 18th.
            body = echo + [name, seg("DMG", "D8", dob, sex), seg("AAA", "N", "", 71, "C")]
            out.append(("sub", body, []))
        elif first == "MARCUS":
            body = echo + [name, seg("REF", "6P", "GRP77015", "MAPLE TOOL AND DIE")] + address + [dmg, seg("DTP", "346", "D8", "20260101")]
            body += [eb(1, "IND", 30, "PR", "GOLD PPO 1500 SECONDARY")]
            body += [eb("R", "", 30, "MB"), seg("REF", "IG", "4TQ8NJ2KR63"), seg("DTP", "290", "D8", "20220701"),
                     seg("LS", "2120"), seg("NM1", "PRP", 2, "MEDICARE PART B", "", "", "", "", "PI", "MCRB1"), seg("LE", "2120")]
            body += [eb("B", "IND", 98, "PR", "", 27, "25", "", "", "", "", "Y"), seg("MSG", "BENEFITS SHOWN ARE SECONDARY TO MEDICARE")]
            out.append(("sub", body, []))
        elif first == "KENJI":
            body = echo + [name, seg("REF", "6P", "GRP77015", "MAPLE TOOL AND DIE")] + address + [dmg, seg("DTP", "346", "D8", "20260101")]
            body += [eb(1, "IND", 30, "PR", "GOLD PPO 1500")]
            body += [eb(1, "IND", 62, "PR", "", "", "", "", "", "", "Y", "Y"), seg("MSG", "PRIOR AUTHORIZATION REQUIRED FOR ADVANCED IMAGING"),
                     seg("LS", "2120"), seg("NM1", "X3", 2, "BLUEWATER UTILIZATION REVIEW"), seg("PER", "IC", "", "TE", "8005550147"), seg("LE", "2120")]
            body += [eb("A", "IND", 62, "PR", "", "", "", ".2", "", "", "", "Y"), eb("C", "IND", 30, "PR", "", 23, "1500", "", "", "", "", "Y"), eb("C", "IND", 30, "PR", "", 29, "310", "", "", "", "", "Y")]
            out.append(("sub", body, []))
        elif first == "ANA":
            body = echo + [name, seg("REF", "6P", "PCAREADV")] + address + [dmg, seg("DTP", "356", "D8", "20260301")]
            body += [eb(1, "IND", 30, "MC", "PRAIRIE CARE ADVANTAGE"), eb("B", "IND", 98, "MC", "", 27, "0", "", "", "", "", "Y"), eb("B", "IND", "MH", "MC", "", 27, "0", "", "", "", "", "Y"),
                     eb("F", "IND", "MH", "MC", "", 23, "", "", "VS", 20, "", "Y"), eb("F", "IND", "MH", "MC", "", 29, "", "", "VS", 8, "", "Y"),
                     seg("MSG", "OUTPATIENT THERAPY VISITS COUNT TOWARD ANNUAL LIMIT")]
            out.append(("sub", body, []))
        elif first == "MATEO":
            body = echo + [name, seg("REF", "6P", "PCAREADV")] + address + [dmg, seg("DTP", "356", "D8", "20260301")]
            body += [eb(1, "IND", 30, "MC", "PRAIRIE CARE ADVANTAGE"), eb("B", "IND", 98, "MC", "", 27, "0", "", "", "", "", "Y"),
                     eb("L", "", "", "MC"), seg("DTP", "295", "D8", "20260301"),
                     seg("MSG", "VISITS TO A PROVIDER OTHER THAN THE ASSIGNED PCP REQUIRE A REFERRAL"), seg("LS", "2120"),
                     seg("NM1", "P3", 1, DR_REYES[0], DR_REYES[1], "", "", "", "XX", DR_REYES[2]), seg("PER", "IC", "", "TE", "2175550188"), seg("LE", "2120")]
            out.append(("sub", body, []))
        elif first == "LOUISE":
            body = echo + [name, seg("REF", "6P", "SMSILVER")] + address + [dmg, seg("DTP", "346", "D8", "20260101")]
            body += [eb(1, "IND", 30, "HM", "SUMMIT SILVER HMO"), eb("B", "IND", 98, "HM", "", 27, "30", "", "", "", "", "Y"),
                     eb("B", "IND", 86, "HM", "", 27, "300", "", "", "", "", "Y"), eb("C", "IND", 30, "HM", "", 23, "0", "", "", "", "", "Y"),
                     eb("G", "IND", 30, "HM", "", 23, "4500", "", "", "", "", "Y"), eb("G", "IND", 30, "HM", "", 29, "4105", "", "", "", "", "Y"),
                     eb("I", "IND", "AL", "HM"),
                     eb("L", "", "", "HM"), seg("LS", "2120"), seg("NM1", "P3", 1, DR_PATEL[0], DR_PATEL[1], "", "", "", "XX", DR_PATEL[2]), seg("LE", "2120")]
            out.append(("sub", body, []))
        elif first == "SIMONE":
            body = echo + [name, seg("AAA", "N", "", 75, "C")]
            out.append(("sub", body, []))
    return out


def plan_ppo(dependent):
    """Bluewater's gold PPO: the benefits of a plan with everything a 271 usually carries."""
    who = "IND"
    return [
        eb(1, "FAM", 30, "PR", "GOLD PPO 1500"),
        eb("B", who, 98, "PR", "", 27, "25", "", "", "", "", "Y"),
        eb("B", who, 98, "PR", "", 27, "60", "", "", "", "", "N"),
        eb("B", who, 86, "PR", "", 27, "250", "", "", "", "", "W"),
        eb("B", who, "UC", "PR", "", 27, "50", "", "", "", "", "Y"),
        eb("C", who, 30, "PR", "", 23, "1500", "", "", "", "", "Y"),
        eb("C", who, 30, "PR", "", 29, "1120", "", "", "", "", "Y"),
        eb("C", "FAM", 30, "PR", "", 23, "3000", "", "", "", "", "Y"),
        eb("C", "FAM", 30, "PR", "", 29, "2400", "", "", "", "", "Y"),
        eb("C", who, 30, "PR", "", 23, "3000", "", "", "", "", "N"),
        eb("A", who, 30, "PR", "", "", "", ".2", "", "", "", "Y"),
        eb("A", who, 30, "PR", "", "", "", ".4", "", "", "", "N"),
        eb("G", who, 30, "PR", "", 23, "6000", "", "", "", "", "Y"),
        eb("G", who, 30, "PR", "", 29, "5210", "", "", "", "", "Y"),
        eb("A", who, 98, "PR", "", "", "", "0", "", "", "", "Y"), seg("MSG", "OFFICE VISIT COPAY ONLY, DEDUCTIBLE WAIVED"),
        eb("I", who, "AL", "PR"),
    ]


def dependent_emma():
    p = next(p for p in PATIENTS if p[3] == "EMMA")
    return [seg("TRN", 2, p[13], CLINIC_TRN), seg("NM1", "03", 1, p[2], p[3]), seg("DMG", "D8", p[5], p[6]), seg("INS", "N", "19"),
            seg("DTP", "346", "D8", "20260101"),
            eb(1, "IND", 30, "PR", "GOLD PPO 1500"), eb("B", "IND", 98, "PR", "", 27, "25", "", "", "", "", "Y"),
            eb("A", "IND", "BZ", "PR", "", "", "", "0", "", "", "", "Y"), seg("MSG", "WELL CHILD CARE COVERED IN FULL IN NETWORK")]


def answer(payer_id, number):
    """One payer's 271."""
    payer = PAYERS[payer_id]
    body = [seg("BHT", "0022", "11", "R{}{:04d}".format(payer_id, number), ASKED, ANSWERED_TIME)]
    body += [seg("HL", 1, "", 20, 1), seg("NM1", "PR", 2, payer, "", "", "", "", "PI", payer_id)]
    if payer_id == "CSMUT":
        # The payer's system is down: the whole answer is a rejection at the information source.
        body += [seg("PER", "IC", "CASCADE PROVIDER SERVICES", "TE", "8005550199"), seg("AAA", "Y", "", 42, "R")]
        return transaction("271", number, body)
    body += [seg("HL", 2, 1, 21, 1), seg("NM1", "1P", 2, CLINIC, "", "", "", "", "XX", CLINIC_NPI)]
    hl = 2
    for _, sub, dependents in answer_people(payer_id):
        hl += 1
        parent = hl
        body.append(seg("HL", hl, 2, 22, 1 if dependents else 0))
        body += sub
        for d in dependents:
            hl += 1
            body.append(seg("HL", hl, parent, 23, 0))
            body += d
    return transaction("271", number, body)


def main():
    os.chdir(HERE)
    with open("clinic-270.x12", "w", newline="\n") as f:
        f.write(wrap(1001, "HS", "MAPLESTFM", "CLEARHOUSE01", inquiries()))
    for n, (payer_id, name) in enumerate(PAYERS.items(), 1):
        with open("{}-271.x12".format(name.split()[0].lower()), "w", newline="\n") as f:
            f.write(wrap(5000 + n, "HB", payer_id + "X", "MAPLESTFM", [answer(payer_id, 1)]))
    with open("appointments.csv", "w", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["Date", "Time", "Last name", "First name", "Middle", "Birth date", "Sex", "Payer ID", "Payer", "Member ID", "Subscriber", "Provider", "Provider NPI", "Visit", "Procedure", "Charge"])
        for p in PATIENTS:
            date, time, last, first, middle, dob, sex, payer, member, provider, visit, procedure, charge, trace, _ = p
            subscriber = "HARTWELL, DANIEL" if first == "EMMA" else "self"
            w.writerow([date, time, last, first, middle, "{}-{}-{}".format(dob[:4], dob[4:6], dob[6:]), sex, payer, PAYERS.get(payer, "Self-pay"), member, subscriber, "{}, {}".format(provider[0], provider[1]), provider[2], visit, procedure, "{:.2f}".format(charge)])
    print("Wrote clinic-270.x12, " + ", ".join("{}-271.x12".format(n.split()[0].lower()) for n in PAYERS.values()) + " and appointments.csv")


if __name__ == "__main__":
    main()
