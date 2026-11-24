namespace AVAEligibility.Core.Eligibility;

/// <summary>
/// The code lists a 270 and 271 use (005010X279A1), worded as X12's published code lists word them. The service type
/// list is the 5010 guide's, because a payer may answer with any of them: checked against the table in CAQH CORE's
/// Eligibility &amp; Benefits Data Content Rule appendix (vEB.2.1, June 2025), whose 179 codes all match these; the
/// eight it doesn't list (21, 22, 85, BA, BJ, BM, BP, BQ) are kept from the guide's list. The other lists hold the codes
/// the guide allows in a 271. Codes are matched as sent, after trimming.
/// </summary>
public static class Codes
{
    /// <summary>EB01: what a benefit line says.</summary>
    public static readonly IReadOnlyDictionary<string, string> Information = new Dictionary<string, string>
    {
        ["1"] = "Active Coverage",
        ["2"] = "Active - Full Risk Capitation",
        ["3"] = "Active - Services Capitated",
        ["4"] = "Active - Services Capitated to Primary Care Physician",
        ["5"] = "Active - Pending Investigation",
        ["6"] = "Inactive",
        ["7"] = "Inactive - Pending Eligibility Update",
        ["8"] = "Inactive - Pending Investigation",
        ["A"] = "Co-Insurance",
        ["B"] = "Co-Payment",
        ["C"] = "Deductible",
        ["CB"] = "Coverage Basis",
        ["D"] = "Benefit Description",
        ["E"] = "Exclusions",
        ["F"] = "Limitations",
        ["G"] = "Out of Pocket (Stop Loss)",
        ["H"] = "Unlimited",
        ["I"] = "Non-Covered",
        ["J"] = "Cost Containment",
        ["K"] = "Reserve",
        ["L"] = "Primary Care Provider",
        ["M"] = "Pre-existing Condition",
        ["MC"] = "Managed Care Coordinator",
        ["N"] = "Services Restricted to Following Provider",
        ["O"] = "Not Deemed a Medical Necessity",
        ["P"] = "Benefit Disclaimer",
        ["Q"] = "Second Surgical Opinion Required",
        ["R"] = "Other or Additional Payor",
        ["S"] = "Prior Year(s) History",
        ["T"] = "Card(s) Reported Lost/Stolen",
        ["U"] = "Contact Following Entity for Eligibility or Benefit Information",
        ["V"] = "Cannot Process",
        ["W"] = "Other Source of Data",
        ["X"] = "Health Care Facility",
        ["Y"] = "Spend Down",
    };

    /// <summary>EB02: whom a benefit covers.</summary>
    public static readonly IReadOnlyDictionary<string, string> CoverageLevel = new Dictionary<string, string>
    {
        ["CHD"] = "Children Only",
        ["DEP"] = "Dependents Only",
        ["ECH"] = "Employee and Children",
        ["EMP"] = "Employee Only",
        ["ESP"] = "Employee and Spouse",
        ["FAM"] = "Family",
        ["IND"] = "Individual",
        ["SPC"] = "Spouse and Children",
        ["SPO"] = "Spouse Only",
    };

    /// <summary>EB03 and EQ01: the kind of care a benefit or a question is about.</summary>
    public static readonly IReadOnlyDictionary<string, string> ServiceType = new Dictionary<string, string>
    {
        ["1"] = "Medical Care", ["2"] = "Surgical", ["3"] = "Consultation", ["4"] = "Diagnostic X-Ray", ["5"] = "Diagnostic Lab",
        ["6"] = "Radiation Therapy", ["7"] = "Anesthesia", ["8"] = "Surgical Assistance", ["9"] = "Other Medical", ["10"] = "Blood Charges",
        ["11"] = "Used Durable Medical Equipment", ["12"] = "Durable Medical Equipment Purchase", ["13"] = "Ambulatory Service Center Facility",
        ["14"] = "Renal Supplies in the Home", ["15"] = "Alternate Method Dialysis", ["16"] = "Chronic Renal Disease (CRD) Equipment",
        ["17"] = "Pre-Admission Testing", ["18"] = "Durable Medical Equipment Rental", ["19"] = "Pneumonia Vaccine", ["20"] = "Second Surgical Opinion",
        ["21"] = "Third Surgical Opinion", ["22"] = "Social Work", ["23"] = "Diagnostic Dental", ["24"] = "Periodontics", ["25"] = "Restorative",
        ["26"] = "Endodontics", ["27"] = "Maxillofacial Prosthetics", ["28"] = "Adjunctive Dental Services", ["30"] = "Health Benefit Plan Coverage",
        ["32"] = "Plan Waiting Period", ["33"] = "Chiropractic", ["34"] = "Chiropractic Office Visits", ["35"] = "Dental Care", ["36"] = "Dental Crowns",
        ["37"] = "Dental Accident", ["38"] = "Orthodontics", ["39"] = "Prosthodontics", ["40"] = "Oral Surgery", ["41"] = "Routine (Preventive) Dental",
        ["42"] = "Home Health Care", ["43"] = "Home Health Prescriptions", ["44"] = "Home Health Visits", ["45"] = "Hospice", ["46"] = "Respite Care",
        ["47"] = "Hospital", ["48"] = "Hospital - Inpatient", ["49"] = "Hospital - Room and Board", ["50"] = "Hospital - Outpatient",
        ["51"] = "Hospital - Emergency Accident", ["52"] = "Hospital - Emergency Medical", ["53"] = "Hospital - Ambulatory Surgical",
        ["54"] = "Long Term Care", ["55"] = "Major Medical", ["56"] = "Medically Related Transportation", ["57"] = "Air Transportation",
        ["58"] = "Cabulance", ["59"] = "Licensed Ambulance", ["60"] = "General Benefits", ["61"] = "In-vitro Fertilization", ["62"] = "MRI/CAT Scan",
        ["63"] = "Donor Procedures", ["64"] = "Acupuncture", ["65"] = "Newborn Care", ["66"] = "Pathology", ["67"] = "Smoking Cessation",
        ["68"] = "Well Baby Care", ["69"] = "Maternity", ["70"] = "Transplants", ["71"] = "Audiology Exam", ["72"] = "Inhalation Therapy",
        ["73"] = "Diagnostic Medical", ["74"] = "Private Duty Nursing", ["75"] = "Prosthetic Device", ["76"] = "Dialysis", ["77"] = "Otological Exam",
        ["78"] = "Chemotherapy", ["79"] = "Allergy Testing", ["80"] = "Immunizations", ["81"] = "Routine Physical", ["82"] = "Family Planning",
        ["83"] = "Infertility", ["84"] = "Abortion", ["85"] = "AIDS", ["86"] = "Emergency Services", ["87"] = "Cancer", ["88"] = "Pharmacy",
        ["89"] = "Free Standing Prescription Drug", ["90"] = "Mail Order Prescription Drug", ["91"] = "Brand Name Prescription Drug",
        ["92"] = "Generic Prescription Drug", ["93"] = "Podiatry", ["94"] = "Podiatry - Office Visits", ["95"] = "Podiatry - Nursing Home Visits",
        ["96"] = "Professional (Physician)", ["97"] = "Anesthesiologist", ["98"] = "Professional (Physician) Visit - Office",
        ["99"] = "Professional (Physician) Visit - Inpatient", ["A0"] = "Professional (Physician) Visit - Outpatient",
        ["A1"] = "Professional (Physician) Visit - Nursing Home", ["A2"] = "Professional (Physician) Visit - Skilled Nursing Facility",
        ["A3"] = "Professional (Physician) Visit - Home", ["A4"] = "Psychiatric", ["A5"] = "Psychiatric - Room and Board", ["A6"] = "Psychotherapy",
        ["A7"] = "Psychiatric - Inpatient", ["A8"] = "Psychiatric - Outpatient", ["A9"] = "Rehabilitation", ["AA"] = "Rehabilitation - Room and Board",
        ["AB"] = "Rehabilitation - Inpatient", ["AC"] = "Rehabilitation - Outpatient", ["AD"] = "Occupational Therapy", ["AE"] = "Physical Medicine",
        ["AF"] = "Speech Therapy", ["AG"] = "Skilled Nursing Care", ["AH"] = "Skilled Nursing Care - Room and Board", ["AI"] = "Substance Abuse",
        ["AJ"] = "Alcoholism", ["AK"] = "Drug Addiction", ["AL"] = "Vision (Optometry)", ["AM"] = "Frames", ["AN"] = "Routine Exam", ["AO"] = "Lenses",
        ["AQ"] = "Nonmedically Necessary Physical", ["AR"] = "Experimental Drug Therapy", ["B1"] = "Burn Care",
        ["B2"] = "Brand Name Prescription Drug - Formulary", ["B3"] = "Brand Name Prescription Drug - Non-Formulary", ["BA"] = "Independent Medical Evaluation",
        ["BB"] = "Partial Hospitalization (Psychiatric)", ["BC"] = "Day Care (Psychiatric)", ["BD"] = "Cognitive Therapy", ["BE"] = "Massage Therapy",
        ["BF"] = "Pulmonary Rehabilitation", ["BG"] = "Cardiac Rehabilitation", ["BH"] = "Pediatric", ["BI"] = "Nursery", ["BJ"] = "Skin",
        ["BK"] = "Orthopedic", ["BL"] = "Cardiac", ["BM"] = "Lymphatic", ["BN"] = "Gastrointestinal", ["BP"] = "Endocrine", ["BQ"] = "Neurology",
        ["BR"] = "Eye", ["BS"] = "Invasive Procedures", ["BT"] = "Gynecological", ["BU"] = "Obstetrical", ["BV"] = "Obstetrical/Gynecological",
        ["BW"] = "Mail Order Prescription Drug: Brand Name", ["BX"] = "Mail Order Prescription Drug: Generic", ["BY"] = "Physician Visit - Office: Sick",
        ["BZ"] = "Physician Visit - Office: Well", ["C1"] = "Coronary Care", ["CA"] = "Private Duty Nursing - Inpatient", ["CB"] = "Private Duty Nursing - Home",
        ["CC"] = "Surgical Benefits - Professional (Physician)", ["CD"] = "Surgical Benefits - Facility", ["CE"] = "Mental Health Provider - Inpatient",
        ["CF"] = "Mental Health Provider - Outpatient", ["CG"] = "Mental Health Facility - Inpatient", ["CH"] = "Mental Health Facility - Outpatient",
        ["CI"] = "Substance Abuse Facility - Inpatient", ["CJ"] = "Substance Abuse Facility - Outpatient", ["CK"] = "Screening X-ray",
        ["CL"] = "Screening laboratory", ["CM"] = "Mammogram, High Risk Patient", ["CN"] = "Mammogram, Low Risk Patient", ["CO"] = "Flu Vaccination",
        ["CP"] = "Eyewear and Eyewear Accessories", ["CQ"] = "Case Management", ["DG"] = "Dermatology", ["DM"] = "Durable Medical Equipment",
        ["DS"] = "Diabetic Supplies", ["GF"] = "Generic Prescription Drug - Formulary", ["GN"] = "Generic Prescription Drug - Non-Formulary",
        ["GY"] = "Allergy", ["IC"] = "Intensive Care", ["MH"] = "Mental Health", ["NI"] = "Neonatal Intensive Care", ["ON"] = "Oncology",
        ["PT"] = "Physical Therapy", ["PU"] = "Pulmonary", ["RN"] = "Renal", ["RT"] = "Residential Psychiatric Treatment", ["TC"] = "Transitional Care",
        ["TN"] = "Transitional Nursery Care", ["UC"] = "Urgent Care",
    };

    /// <summary>EB04: the kind of insurance.</summary>
    public static readonly IReadOnlyDictionary<string, string> InsuranceType = new Dictionary<string, string>
    {
        ["12"] = "Medicare Secondary Working Aged Beneficiary or Spouse with Employer Group Health Plan",
        ["13"] = "Medicare Secondary End-Stage Renal Disease Beneficiary in the Mandated Coordination Period with an Employer's Group Health Plan",
        ["14"] = "Medicare Secondary, No-fault Insurance including Auto is Primary",
        ["15"] = "Medicare Secondary Worker's Compensation",
        ["16"] = "Medicare Secondary Public Health Service (PHS) or Other Federal Agency",
        ["41"] = "Medicare Secondary Black Lung",
        ["42"] = "Medicare Secondary Veteran's Administration",
        ["43"] = "Medicare Secondary Disabled Beneficiary Under Age 65 with Large Group Health Plan (LGHP)",
        ["47"] = "Medicare Secondary, Other Liability Insurance is Primary",
        ["AP"] = "Auto Insurance Policy", ["C1"] = "Commercial", ["CO"] = "Consolidated Omnibus Budget Reconciliation Act (COBRA)",
        ["CP"] = "Medicare Conditionally Primary", ["D"] = "Disability", ["DB"] = "Disability Benefits", ["EP"] = "Exclusive Provider Organization",
        ["FF"] = "Family or Friends", ["GP"] = "Group Policy", ["HM"] = "Health Maintenance Organization (HMO)",
        ["HN"] = "Health Maintenance Organization (HMO) - Medicare Risk", ["HS"] = "Special Low Income Medicare Beneficiary", ["IN"] = "Indemnity",
        ["IP"] = "Individual Policy", ["LC"] = "Long Term Care", ["LD"] = "Long Term Policy", ["LI"] = "Life Insurance", ["LT"] = "Litigation",
        ["MA"] = "Medicare Part A", ["MB"] = "Medicare Part B", ["MC"] = "Medicaid", ["MH"] = "Medigap Part A", ["MI"] = "Medigap Part B",
        ["MP"] = "Medicare Primary", ["OT"] = "Other", ["PE"] = "Property Insurance - Personal", ["PL"] = "Personal",
        ["PP"] = "Personal Payment (Cash - No Insurance)", ["PR"] = "Preferred Provider Organization (PPO)", ["PS"] = "Point of Service (POS)",
        ["QM"] = "Qualified Medicare Beneficiary", ["RP"] = "Property Insurance - Real", ["SP"] = "Supplemental Policy",
        ["TF"] = "Tax Equity Fiscal Responsibility Act (TEFRA)", ["WC"] = "Workers Compensation", ["WU"] = "Wrap Up Policy",
    };

    /// <summary>EB06 and HSD05: the period an amount or a count is for.</summary>
    public static readonly IReadOnlyDictionary<string, string> TimePeriod = new Dictionary<string, string>
    {
        ["6"] = "Hour", ["7"] = "Day", ["13"] = "24 Hours", ["21"] = "Years", ["22"] = "Service Year", ["23"] = "Calendar Year", ["24"] = "Year to Date",
        ["25"] = "Contract", ["26"] = "Episode", ["27"] = "Visit", ["28"] = "Outlier", ["29"] = "Remaining", ["30"] = "Exceeded", ["31"] = "Not Exceeded",
        ["32"] = "Lifetime", ["33"] = "Lifetime Remaining", ["34"] = "Month", ["35"] = "Week", ["36"] = "Admission",
    };

    /// <summary>EB09 and HSD01: what a quantity counts.</summary>
    public static readonly IReadOnlyDictionary<string, string> Quantity = new Dictionary<string, string>
    {
        ["8H"] = "Minimum", ["99"] = "Quantity Used", ["CA"] = "Covered - Actual", ["CE"] = "Covered - Estimated", ["D3"] = "Number of Co-insurance Days",
        ["DB"] = "Deductible Blood Units", ["DY"] = "Days", ["HS"] = "Hours", ["LA"] = "Life-time Reserve - Actual", ["LE"] = "Life-time Reserve - Estimated",
        ["M2"] = "Maximum", ["MN"] = "Month", ["P6"] = "Number of Services or Procedures", ["QA"] = "Quantity Approved", ["S7"] = "Age, High Value",
        ["S8"] = "Age, Low Value", ["VS"] = "Visits", ["YY"] = "Years", ["FL"] = "Units",
    };

    /// <summary>EB11 (authorization or certification required) and EB12 (in plan network).</summary>
    public static readonly IReadOnlyDictionary<string, string> YesNo = new Dictionary<string, string>
    {
        ["Y"] = "Yes", ["N"] = "No", ["U"] = "Unknown", ["W"] = "Not Applicable",
    };

    /// <summary>AAA03: why a request couldn't be answered, as the payer says it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Reject = new Dictionary<string, string>
    {
        ["04"] = "Authorized Quantity Exceeded",
        ["15"] = "Required application data missing",
        ["41"] = "Authorization/Access Restrictions",
        ["42"] = "Unable to Respond at Current Time",
        ["43"] = "Invalid/Missing Provider Identification",
        ["44"] = "Invalid/Missing Provider Name",
        ["45"] = "Invalid/Missing Provider Specialty",
        ["46"] = "Invalid/Missing Provider Phone Number",
        ["47"] = "Invalid/Missing Provider State",
        ["48"] = "Invalid/Missing Referring Provider Identification Number",
        ["49"] = "Provider is Not Primary Care Physician",
        ["50"] = "Provider Ineligible for Inquiries",
        ["51"] = "Provider Not on File",
        ["52"] = "Service Dates Not Within Provider Plan Enrollment",
        ["53"] = "Inquired Benefit Inconsistent with Provider Type",
        ["54"] = "Inappropriate Product/Service ID Qualifier",
        ["55"] = "Inappropriate Product/Service ID",
        ["56"] = "Inappropriate Date",
        ["57"] = "Invalid/Missing Date(s) of Service",
        ["58"] = "Invalid/Missing Date-of-Birth",
        ["60"] = "Date of Birth Follows Date(s) of Service",
        ["61"] = "Date of Death Precedes Date(s) of Service",
        ["62"] = "Date of Service Not Within Allowable Inquiry Period",
        ["63"] = "Date of Service in Future",
        ["64"] = "Invalid/Missing Patient ID",
        ["65"] = "Invalid/Missing Patient Name",
        ["66"] = "Invalid/Missing Patient Gender Code",
        ["67"] = "Patient Not Found",
        ["68"] = "Duplicate Patient ID Number",
        ["69"] = "Inconsistent with Patient's Age",
        ["70"] = "Inconsistent with Patient's Gender",
        ["71"] = "Patient Birth Date Does Not Match That for the Patient on the Database",
        ["72"] = "Invalid/Missing Subscriber/Insured ID",
        ["73"] = "Invalid/Missing Subscriber/Insured Name",
        ["74"] = "Invalid/Missing Subscriber/Insured Gender Code",
        ["75"] = "Subscriber/Insured Not Found",
        ["76"] = "Duplicate Subscriber/Insured ID Number",
        ["77"] = "Subscriber Found, Patient Not Found",
        ["78"] = "Subscriber/Insured Not in Group/Plan Identified",
        ["79"] = "Invalid Participant Identification",
        ["80"] = "No Response received - Transaction Terminated",
        ["97"] = "Invalid or Missing Provider Address",
        ["AA"] = "Authorization Number Not Found",
        ["AE"] = "Requires Primary Care Physician Authorization",
        ["AF"] = "Invalid/Missing Diagnosis Code(s)",
        ["AG"] = "Invalid/Missing Procedure Code(s)",
        ["AO"] = "Additional Patient Condition Information Required",
        ["CI"] = "Certification Information Does Not Match Patient",
        ["E8"] = "Requires Medical Review",
        ["IA"] = "Invalid Authorization Number Format",
        ["MA"] = "Missing Authorization Number",
        ["T4"] = "Payer Name or Identifier Missing",
    };

    /// <summary>AAA04: what to do about a rejection.</summary>
    public static readonly IReadOnlyDictionary<string, string> FollowUp = new Dictionary<string, string>
    {
        ["C"] = "Please Correct and Resubmit",
        ["N"] = "Resubmission Not Allowed",
        ["P"] = "Please Resubmit Original Transaction",
        ["R"] = "Resubmission Allowed",
        ["S"] = "Do Not Resubmit; Inquiry Initiated to a Third Party",
        ["W"] = "Please Wait 30 Days and Resubmit",
        ["X"] = "Please Wait 10 Days and Resubmit",
        ["Y"] = "Do Not Resubmit; We Will Hand Over Response",
    };

    /// <summary>DTP01: what a date is.</summary>
    public static readonly IReadOnlyDictionary<string, string> DateKind = new Dictionary<string, string>
    {
        ["096"] = "Discharge", ["102"] = "Issue", ["152"] = "Effective Date of Change", ["193"] = "Period Start", ["194"] = "Period End",
        ["198"] = "Completion", ["290"] = "Coordination of Benefits", ["291"] = "Plan", ["292"] = "Benefit", ["295"] = "Primary Care Provider",
        ["304"] = "Latest Visit or Consultation", ["307"] = "Eligibility", ["318"] = "Added",
        ["340"] = "Consolidated Omnibus Budget Reconciliation Act (COBRA) Begin", ["341"] = "Consolidated Omnibus Budget Reconciliation Act (COBRA) End",
        ["342"] = "Premium Paid to Date Begin", ["343"] = "Premium Paid to Date End", ["346"] = "Plan Begin", ["347"] = "Plan End",
        ["348"] = "Benefit Begin", ["349"] = "Benefit End", ["356"] = "Eligibility Begin", ["357"] = "Eligibility End", ["382"] = "Enrollment",
        ["435"] = "Admission", ["442"] = "Date of Death", ["458"] = "Certification", ["472"] = "Service", ["539"] = "Policy Effective",
        ["540"] = "Policy Expiration", ["636"] = "Date of Last Update", ["771"] = "Status",
    };

    /// <summary>REF01: what an identifier is.</summary>
    public static readonly IReadOnlyDictionary<string, string> Reference = new Dictionary<string, string>
    {
        ["18"] = "Plan Number", ["1L"] = "Group or Policy Number", ["1W"] = "Member Identification Number", ["3H"] = "Case Number",
        ["49"] = "Family Unit Number", ["6P"] = "Group Number", ["9F"] = "Referral Number", ["ALS"] = "Alternative List ID", ["CE"] = "Class of Contract Code",
        ["CLI"] = "Coverage List ID", ["CT"] = "Contract Number", ["EA"] = "Medical Record Identification Number", ["EJ"] = "Patient Account Number",
        ["F6"] = "Health Insurance Claim (HIC) Number", ["FO"] = "Drug Formulary Number", ["G1"] = "Prior Authorization Number",
        ["GH"] = "Identification Card Serial Number", ["HJ"] = "Identity Card Number", ["HPI"] = "Centers for Medicare and Medicaid Services National Provider Identifier",
        ["IF"] = "Issue Number", ["IG"] = "Insurance Policy Number", ["M7"] = "Medical Assistance Category", ["MRC"] = "Eligibility Category",
        ["N6"] = "Plan Network Identification Number", ["NQ"] = "Medicaid Recipient Identification Number", ["Q4"] = "Prior Identifier Number",
        ["SY"] = "Social Security Number", ["Y4"] = "Agency Claim Number",
    };

    /// <summary>NM101: who a name is.</summary>
    public static readonly IReadOnlyDictionary<string, string> Entity = new Dictionary<string, string>
    {
        ["03"] = "Dependent", ["13"] = "Contracted Service Provider", ["1I"] = "Preferred Provider Organization (PPO)", ["1P"] = "Provider",
        ["2B"] = "Third-Party Administrator", ["36"] = "Employer", ["73"] = "Other Physician", ["80"] = "Hospital", ["FA"] = "Facility",
        ["GP"] = "Gateway Provider", ["GW"] = "Group", ["I3"] = "Independent Physicians Association (IPA)", ["IL"] = "Insured or Subscriber",
        ["LR"] = "Legal Representative", ["P3"] = "Primary Care Provider", ["P4"] = "Prior Insurance Carrier", ["P5"] = "Plan Sponsor",
        ["PR"] = "Payer", ["PRP"] = "Primary Payer", ["SEP"] = "Secondary Payer", ["TTP"] = "Tertiary Payer", ["VN"] = "Vendor",
        ["X3"] = "Utilization Management Organization", ["Y2"] = "Managed Care Organization",
    };

    /// <summary>INS02: how a person is related to the subscriber.</summary>
    public static readonly IReadOnlyDictionary<string, string> Relationship = new Dictionary<string, string>
    {
        ["01"] = "Spouse", ["18"] = "Self", ["19"] = "Child", ["20"] = "Employee", ["21"] = "Unknown", ["39"] = "Organ Donor", ["40"] = "Cadaver Donor",
        ["53"] = "Life Partner", ["G8"] = "Other Relationship",
    };

    /// <summary>HL03: the level a loop is about.</summary>
    public static readonly IReadOnlyDictionary<string, string> Level = new Dictionary<string, string>
    {
        ["20"] = "Information Source", ["21"] = "Information Receiver", ["22"] = "Subscriber", ["23"] = "Dependent",
    };

    /// <summary>The meaning of <paramref name="code"/> in <paramref name="list"/>, or null when it isn't one of them.</summary>
    public static string? Meaning(this IReadOnlyDictionary<string, string> list, string? code) =>
        code != null && list.TryGetValue(code.Trim(), out var meaning) ? meaning : null;

    /// <summary>The meaning, or the code itself (marked unknown) when the list hasn't got it.</summary>
    public static string Say(this IReadOnlyDictionary<string, string> list, string code) =>
        list.Meaning(code) ?? $"code {code.Trim()} (not in the list)";
}
