-- =============================================================================
-- Demo seed data for Agent Control Panel
-- Restores the sample Skill, Agent, and Knowledge Base entry used in the README.
--
-- Run against a database that already has the schema applied (start the app once
-- so EF Core migrations create the tables), then:
--
--   docker exec -i agent-control-panel-db \
--     psql -U postgres -d AgentControlPanel < Data/seed-demo-data.sql
--
-- The script is idempotent: re-running it will not create duplicates.
-- =============================================================================

INSERT INTO "Skills" ("Name","SkillMd","ScriptsJson","IsAIGenerated","CreatedAt")
SELECT 'npi-registry-lookup', '---
name: npi-registry-lookup
description: Searches for healthcare providers and organizations in the U.S. NPI Registry using the official CMS API. Use when the user needs to find a doctor, physician, hospital, or other healthcare provider by name, NPI number, location (city, state, postal code), specialty/taxonomy, or organization name. Returns provider details including NPI, name, credentials, addresses, phone numbers, and taxonomy classifications.
---

# NPI Registry Lookup

## Quick Start
This skill queries the official CMS NPI Registry API (https://npiregistry.cms.hhs.gov/api) to search for healthcare providers (individuals) and organizations by a variety of parameters. No API key is required.

## Usage
Call the `npi_registry_lookup__search` tool with one or more search parameters. The script returns formatted JSON results.

### Common parameters
- `number`: A specific 10-digit NPI number (exact match).
- `first_name` / `last_name`: For individual providers (NPI-1). Supports trailing wildcard `*`.
- `organization_name`: For organizations (NPI-2). Supports trailing wildcard `*`.
- `city`, `state` (2-letter code), `postal_code`: Location filters.
- `taxonomy_description`: Specialty/taxonomy (e.g. "Internal Medicine", "Pediatrics").
- `enumeration_type`: `NPI-1` (individuals) or `NPI-2` (organizations).
- `limit`: Max results (1-200, default 10).

## Examples

1. Find a doctor by name in a state:
   - `last_name=Smith`, `state=CA`, `taxonomy_description=Cardiology`

2. Look up by NPI number:
   - `number=1234567890`

3. Find hospitals in a city:
   - `organization_name=General*`, `city=Boston`, `state=MA`, `enumeration_type=NPI-2`

4. Wildcard name search:
   - `last_name=John*`, `state=NY`

## Edge Cases & Notes
- At least one search parameter (besides location-only in some cases) is required. The API requires a meaningful filter; provide name, number, organization, or taxonomy along with location.
- Wildcards (`*`) require at least 2 preceding characters and only work at the end of a value.
- `state` must be a 2-letter USPS abbreviation; `country_code` defaults to `US`.
- If no results are found, the API returns `result_count: 0`; report this clearly.
- The API caps results at 200 and uses a `skip` parameter for pagination.
- API version is fixed at `2.1`.

## Workflow
1. Collect the user''s intent and map it to parameters (individual vs organization).
2. Run the search script.
3. Summarize key fields: NPI number, name/credential, primary taxonomy, practice address, and phone.
4. If too many results, suggest narrowing by adding city, state, or specialty.
', E'[
    {
      "name": "search.py",
      "language": "python",
      "code": "import os\\nimport json\\nimport urllib.parse\\nimport urllib.request\\n\\nAPI_URL = \\"https://npiregistry.cms.hhs.gov/api/\\"\\n\\n\\ndef build_params():\\n    params = {\\"version\\": \\"2.1\\"}\\n    mapping = {\\n        \\"number\\": \\"number\\",\\n        \\"enumeration_type\\": \\"enumeration_type\\",\\n        \\"first_name\\": \\"first_name\\",\\n        \\"last_name\\": \\"last_name\\",\\n        \\"organization_name\\": \\"organization_name\\",\\n        \\"taxonomy_description\\": \\"taxonomy_description\\",\\n        \\"city\\": \\"city\\",\\n        \\"state\\": \\"state\\",\\n        \\"postal_code\\": \\"postal_code\\",\\n        \\"country_code\\": \\"country_code\\",\\n    }\\n    for env_key, api_key in mapping.items():\\n        val = os.environ.get(env_key, \\"\\").strip()\\n        if val:\\n            params[api_key] = val\\n\\n    # limit\\n    limit = os.environ.get(\\"limit\\", \\"\\").strip()\\n    if limit:\\n        try:\\n            limit_int = max(1, min(200, int(limit)))\\n        except ValueError:\\n            limit_int = 10\\n    else:\\n        limit_int = 10\\n    params[\\"limit\\"] = str(limit_int)\\n\\n    skip = os.environ.get(\\"skip\\", \\"\\").strip()\\n    if skip:\\n        try:\\n            params[\\"skip\\"] = str(max(0, int(skip)))\\n        except ValueError:\\n            pass\\n\\n    return params\\n\\n\\ndef summarize(result):\\n    out = []\\n    for r in result.get(\\"results\\", []):\\n        basic = r.get(\\"basic\\", {})\\n        name = basic.get(\\"organization_name\\") or \\" \\".join(\\n            filter(None, [basic.get(\\"first_name\\"), basic.get(\\"last_name\\")])\\n        )\\n        credential = basic.get(\\"credential\\", \\"\\")\\n        taxonomies = r.get(\\"taxonomies\\", [])\\n        primary_tax = next(\\n            (t.get(\\"desc\\") for t in taxonomies if t.get(\\"primary\\")),\\n            taxonomies[0].get(\\"desc\\") if taxonomies else \\"\\",\\n        )\\n        addresses = r.get(\\"addresses\\", [])\\n        location = next(\\n            (a for a in addresses if a.get(\\"address_purpose\\") == \\"LOCATION\\"),\\n            addresses[0] if addresses else {},\\n        )\\n        address_str = \\", \\".join(\\n            filter(\\n                None,\\n                [\\n                    location.get(\\"address_1\\"),\\n                    location.get(\\"city\\"),\\n                    location.get(\\"state\\"),\\n                    location.get(\\"postal_code\\"),\\n                ],\\n            )\\n        )\\n        out.append(\\n            {\\n                \\"npi\\": r.get(\\"number\\"),\\n                \\"type\\": r.get(\\"enumeration_type\\"),\\n                \\"name\\": name,\\n                \\"credential\\": credential,\\n                \\"primary_taxonomy\\": primary_tax,\\n                \\"address\\": address_str,\\n                \\"phone\\": location.get(\\"telephone_number\\", \\"\\"),\\n            }\\n        )\\n    return out\\n\\n\\ndef main():\\n    params = build_params()\\n    has_filter = any(\\n        k in params\\n        for k in [\\n            \\"number\\",\\n            \\"first_name\\",\\n            \\"last_name\\",\\n            \\"organization_name\\",\\n            \\"taxonomy_description\\",\\n            \\"city\\",\\n            \\"state\\",\\n            \\"postal_code\\",\\n        ]\\n    )\\n    if not has_filter:\\n        print(json.dumps({\\"error\\": \\"At least one search parameter is required (e.g. last_name, number, organization_name, state).\\"}))\\n        return\\n\\n    query = urllib.parse.urlencode(params)\\n    url = API_URL + \\"?\\" + query\\n    try:\\n        req = urllib.request.Request(url, headers={\\"User-Agent\\": \\"npi-registry-lookup/1.0\\"})\\n        with urllib.request.urlopen(req, timeout=30) as resp:\\n            data = json.loads(resp.read().decode(\\"utf-8\\"))\\n    except Exception as e:\\n        print(json.dumps({\\"error\\": f\\"Request failed: {e}\\", \\"url\\": url}))\\n        return\\n\\n    if \\"Errors\\" in data and data[\\"Errors\\"]:\\n        print(json.dumps({\\"error\\": data[\\"Errors\\"], \\"url\\": url}))\\n        return\\n\\n    output = {\\n        \\"result_count\\": data.get(\\"result_count\\", 0),\\n        \\"providers\\": summarize(data),\\n        \\"query_url\\": url,\\n    }\\n    print(json.dumps(output, indent=2))\\n\\n\\nif __name__ == \\"__main__\\":\\n    main()\\n",
      "parameters": [
        {
          "name": "number",
          "description": "A specific 10-digit NPI number for exact lookup.",
          "type": "string",
          "required": false
        },
        {
          "name": "enumeration_type",
          "description": "Provider type: ''NPI-1'' for individuals or ''NPI-2'' for organizations.",
          "type": "string",
          "required": false
        },
        {
          "name": "first_name",
          "description": "First name of an individual provider. Supports trailing wildcard ''*''.",
          "type": "string",
          "required": false
        },
        {
          "name": "last_name",
          "description": "Last name of an individual provider. Supports trailing wildcard ''*''.",
          "type": "string",
          "required": false
        },
        {
          "name": "organization_name",
          "description": "Name of the healthcare organization. Supports trailing wildcard ''*''.",
          "type": "string",
          "required": false
        },
        {
          "name": "taxonomy_description",
          "description": "Specialty/taxonomy description, e.g. ''Internal Medicine'' or ''Cardiology''.",
          "type": "string",
          "required": false
        },
        {
          "name": "city",
          "description": "City of the provider''s practice location.",
          "type": "string",
          "required": false
        },
        {
          "name": "state",
          "description": "Two-letter USPS state abbreviation, e.g. ''CA''.",
          "type": "string",
          "required": false
        },
        {
          "name": "postal_code",
          "description": "Postal/ZIP code of the provider''s location.",
          "type": "string",
          "required": false
        },
        {
          "name": "country_code",
          "description": "Two-letter country code (defaults to US on the API side).",
          "type": "string",
          "required": false
        },
        {
          "name": "limit",
          "description": "Maximum number of results to return (1-200, default 10).",
          "type": "number",
          "required": false
        },
        {
          "name": "skip",
          "description": "Number of results to skip for pagination.",
          "type": "number",
          "required": false
        }
      ]
    }
  ]', true, now()
WHERE NOT EXISTS (SELECT 1 FROM "Skills" WHERE "Name"='npi-registry-lookup');

INSERT INTO "Agents" ("Name","Description","SystemPrompt","Model","KnowledgeBaseEnabled","CreatedAt")
SELECT 'Symptom-to-Specialist Finder', 'Helps users find the right type of U.S. healthcare provider for their symptoms. It maps described symptoms to the appropriate medical specialty, then searches the official CMS NPI Registry for real, licensed providers near the user. Informational only — not medical advice or diagnosis.', 'You are the Symptom-to-Specialist Finder, an assistant that helps people in the United States find the RIGHT KIND of doctor for their symptoms and then locates real, licensed providers near them.

## What you do
1. Listen to the user''s described symptoms or health concern.
2. Determine the most appropriate medical specialty (taxonomy) for that concern.
3. Collect the user''s location — you need at least a 2-letter US state (city and/or postal code make results better).
4. Use the `npi-registry-lookup` skill to search the official CMS NPI Registry for matching providers, passing `taxonomy_description` (the specialty), `state`, and `city` when available, with `enumeration_type=NPI-1` for individual doctors.
5. Present the results clearly: name + credentials, specialty, practice address, phone, and NPI number. Offer to narrow by city or specialty if there are too many.

## Mapping symptoms to a specialty
Infer the specialty from the symptom, e.g.:
- Chest pain, palpitations, high blood pressure → "Cardiovascular Disease" (or "Internal Medicine")
- Skin rash, acne, moles → "Dermatology"
- Joint, bone, or back pain, sports injury → "Orthopaedic Surgery"
- Headaches, numbness, seizures, dizziness → "Neurology"
- Children''s health → "Pediatrics"
- Anxiety, depression, mental health → "Psychiatry"
- Ear/nose/throat, sinus, hearing → "Otolaryngology"
- Digestive issues, abdominal pain → "Gastroenterology"
- General checkup, undifferentiated symptoms → "Family Medicine" or "Internal Medicine"
Use standard NPI taxonomy descriptions. If a search returns no results, try a broader specialty (e.g. "Internal Medicine") or remove the city filter, and tell the user what you adjusted.

## How to use the tool
- The NPI Registry requires a meaningful filter — never search on location alone. Always include the specialty (and ideally a name if the user has one).
- `state` must be a 2-letter USPS code (e.g. CA, NY, TX). The registry covers U.S. providers only.
- If the user hasn''t given a location, ask for at least their state before searching.

## Knowledge base
If a search_knowledge_base tool is available, use it for any stored, organization-specific information (e.g. in-network providers, clinic policies) before or alongside the registry search.

## Safety and boundaries — important
- You are NOT a doctor. Do not diagnose conditions or recommend treatments or medications.
- Frame everything as "the type of specialist commonly seen for this" — not a diagnosis.
- For severe or emergency symptoms (chest pain with shortness of breath, signs of stroke, severe bleeding, suicidal thoughts, difficulty breathing), tell the user to call 911 or go to the nearest emergency room immediately, and do not just hand them a provider list.
- Remind users to confirm a provider''s current details, availability, and insurance acceptance directly with the provider''s office.
- Be warm, clear, and concise.', 'claude-haiku-4-5', true, now()
WHERE NOT EXISTS (SELECT 1 FROM "Agents" WHERE "Name"='Symptom-to-Specialist Finder');

INSERT INTO "AgentSkill" ("AgentsId","SkillsId")
SELECT a."Id", s."Id" FROM "Agents" a, "Skills" s
WHERE a."Name"='Symptom-to-Specialist Finder' AND s."Name"='npi-registry-lookup'
  AND NOT EXISTS (SELECT 1 FROM "AgentSkill" j WHERE j."AgentsId"=a."Id" AND j."SkillsId"=s."Id");

INSERT INTO "KnowledgeDocuments" ("Title","Content","Embedding","CreatedAt")
SELECT 'How to prepare for a specialist visit', 'Use this checklist to help users prepare for an appointment with a specialist.

Before the visit:
- Confirm the appointment date, time, and exact location, and ask how early to arrive.
- Verify the provider is in-network and that a referral or prior authorization isn''t required by your insurance.
- Ask whether any prep is needed (e.g., fasting for bloodwork, stopping a medication, or arranging a ride).

What to bring:
- Insurance card and a photo ID.
- A list of all current medications, vitamins, and supplements, with dosages.
- A written list of your symptoms, including when they started, how often they occur, what makes them better or worse, and how severe they are.
- Relevant medical records, prior test results, and imaging (or the facility/date so they can be requested).
- A list of past surgeries, major illnesses, allergies, and your family medical history.
- The name and contact info of your primary care provider and any referring doctor.

Questions to prepare:
- What could be causing my symptoms, and what tests might be needed?
- What are my treatment options, and what are the risks and benefits?
- Are there lifestyle changes that would help?
- When should I follow up, and what warning signs should prompt me to call sooner?

During the visit:
- Be specific and honest about symptoms; don''t minimize them.
- Take notes or bring someone to help you remember.
- Before leaving, make sure you understand the diagnosis (if any), next steps, and any prescriptions.

Note: This is general guidance to help prepare for an appointment. It is not medical advice. For emergencies, call 911.', '[0.031974826,-0.005057243,0.045460805,0.019032635,-0.018923877,-0.036107626,-0.010332001,0.038935333,-0.010495138,0.027298236,-0.032844886,0.047200933,-0.020664003,0.049158577,-0.008972527,-0.009570696,0.034367498,-0.009407559,0.02914712,0.021969097,0.021207793,0.022512887,-0.007178022,-0.03915285,-0.041763037,-0.016966233,0.016096171,-0.026210656,-0.024035498,0.011745854,0.007939328,0.015226107,-0.008700632,-0.000659345,-0.002800516,-0.048723545,-0.009081285,-0.05785921,-0.022621645,-0.023817983,0.000441829,0.016966233,-0.015878655,-0.051333737,-0.004866917,0.025558108,-0.073085316,0.002215942,0.003480253,0.023709225,-0.01087579,8.2843e-05,-0.011800233,0.007667433,-0.055249017,-0.003453064,0.04459074,-0.04415571,0.02490556,0.03175731,0.04633087,0.037195206,-0.03480253,0.032409858,-0.011908991,-0.01816257,0.015769897,0.010223243,0.03480253,0.028277056,0.02588438,-0.007721812,0.020555245,0.000113006,0.04524329,0.013594738,0.003276332,0.030017182,0.039370365,0.003724958,-0.004866917,0.032192342,0.026754444,-0.014138528,-0.05872927,0.081350915,-0.031974826,-0.013377222,0.051333737,-0.019358909,-0.028712088,0.03066973,0.027406992,-0.02501432,-0.015334865,0.00467659,-0.043285646,0.015226107,-0.004622211,0.023600467,-0.02077276,0.02958215,0.010658275,-0.004866917,0.01870636,-0.003806527,-0.001957642,0.007232401,-0.027624508,-0.046548385,0.043720677,-0.02175158,-0.015334865,-0.003860906,0.033497434,-0.022404129,0.02958215,0.015117349,0.004214369,0.066124804,0.007721812,-0.019358909,-0.007014885,0.016204929,0.008646253,-0.05198628,0.019467667,0.019685183,-0.018053813,-0.022621645,0.020337729,-0.0385003,-0.014682317,-0.010603896,-0.039805394,0.030234698,-0.015334865,0.001726532,-0.007667433,0.015769897,-0.019032635,0.015769897,-0.015443623,0.009733833,0.07178022,0.033062402,0.017727539,0.015661139,0.002732542,-0.020337729,0.05350889,-0.05611908,-0.011365201,-0.00831998,-0.03175731,0.018380087,-0.009026906,0.05829424,-0.028277056,0.01141958,0.004839727,-0.00470378,0.02958215,-0.002882085,0.08309104,0.031104762,-0.007993707,-0.05568405,-0.013866633,-0.02958215,-0.028277056,-0.011310822,-0.030452214,-0.003425874,-0.025666868,0.008700632,0.014791075,0.036107626,0.001597382,-0.026863204,-0.041763037,-0.016748717,-0.007014885,-0.006362338,-0.020337729,-0.018923877,-0.009244422,-0.034367498,0.048288513,0.04785348,-0.023274193,-0.059599333,-0.01250716,-0.017401265,-0.04002291,0.047200933,-0.040240426,0.00831998,-0.002433458,-0.000849671,-0.027298236,-0.003561822,-0.00728678,0.04459074,0.041980553,-0.013322844,-0.05176876,0.000475816,-0.00674299,-0.018053813,-0.028929604,-0.022186613,-0.001529408,0.00880939,-0.001821695,0.002379079,-0.042415585,-0.024144256,0.014682317,0.034149982,-0.012017749,0.027733266,0.008048085,0.004975674,-0.033497434,-0.026319413,0.044808257,-0.001209932,-0.06786493,0.021099035,-0.040675458,0.02490556,0.064819716,-0.00935318,-0.005818548,0.003181169,-0.000805488,0.033279918,-0.004866917,-0.008265601,0.056554113,-0.000846273,-0.015334865,0.005900117,-0.023709225,0.016531201,-0.002256727,-0.050463673,0.036107626,-0.02805954,0.028712088,0.002882085,0.025123077,0.013377222,0.031104762,0.00880939,0.010821411,0.035455078,0.033497434,0.0385003,-0.051333737,0.033062402,0.032192342,0.059599333,0.045025773,-0.081350915,0.037630238,-0.003969664,-0.000375555,0.02283916,-0.03371495,0.007667433,-0.037630238,-0.015661139,-0.016422443,0.021860339,-0.012180886,0.03915285,-0.022512887,-0.024796804,0.017401265,0.03066973,0.064384684,0.006199201,0.068299964,0.013268464,0.052421313,-0.023274193,0.018923877,-0.045460805,-0.04219807,0.014899833,0.030234698,-0.015552381,-0.013105328,-0.044373225,-0.028494572,0.019576425,0.019467667,0.021425309,0.03741272,-0.006307959,-0.006389527,-0.008374359,0.037630238,0.06568977,-0.03066973,-0.03806527,-0.032192342,0.026428172,-0.027515752,-0.002895679,-0.007776191,0.036760174,0.019793939,0.017074991,-0.014356044,-0.030887246,-0.001733329,-0.06525475,-0.008918148,-0.008700632,0.001944048,-0.007341159,0.003262737,0.042415585,0.000584574,0.018815119,0.015226107,-0.053073857,-0.014247286,-0.02131655,-0.002528621,-0.006579853,-0.03915285,0.039587878,0.020664003,-0.05198628,-0.052856345,0.026645688,0.004404695,0.0770006,-0.053291377,-0.042850617,-0.014682317,-0.027733266,0.016966233,0.05416144,0.032844886,-0.06568977,0.048288513,0.03589011,0.009842591,-0.01294219,0.04981112,0.013921012,0.001876074,-0.019685183,0.020337729,-0.020011455,0.016096171,0.014138528,-0.035237562,-0.0852662,-0.05350889,0.030234698,0.027733266,-0.001617774,0.064384684,0.050463673,-0.017727539,-0.052421313,-0.01402977,-0.04741845,0.023165435,-0.024470529,0.004812538,0.048070997,0.028712088,0.040675458,-0.02490556,-0.006389527,-0.073085316,0.010549517,-0.041980553,0.004051232,0.04567832,0.013159706,0.030017182,-0.02697196,-0.054596473,-0.034367498,0.001271108,0.05568405,-0.032192342,0.004377506,-0.00622639,-0.009516317,-0.06394965,0.085701235,0.02958215,-0.026754444,0.032844886,0.026645688,0.027624508,-0.00935318,-0.07004009,-0.000897253,0.02588438,-0.020446487,-0.001862479,0.021207793,-0.021534065,0.004241558,0.021860339,0.037630238,-0.005927306,0.002202348,0.041763037,0.008972527,-0.00069673,0.037195206,-0.000196274,0.016639959,0.024688045,0.05829424,-0.00312679,-0.06699487,-0.037195206,-0.040240426,0.039805394,0.00364339,-0.007993707,0.014464802,0.027624508,0.020446487,-0.024035498,-0.019141393,-0.028277056,-0.031974826,-0.048288513,-0.034585014,-0.000849671,0.002732542,-0.047635965,0.01925015,-0.023165435,-0.001719735,-0.037630238,0.015878655,-0.051116217,0.04567832,0.01870636,0.03741272,-0.009135664,-0.001155553,-0.032627374,0.01457356,0.050463673,-0.027733266,-0.001794506,0.007232401,-0.04459074,-0.00622639,0.027842024,0.011310822,-0.019358909,0.02805954,0.062209524,0.035020046,0.022512887,-0.06307959,0.024470529,-0.024579288,-0.021425309,0.007341159,0.000338169,-0.059599333,-0.062209524,0.00677018,-0.006498285,0.015987413,0.019902697,-0.029364634,0.02958215,-0.005818548,0.013214086,-0.009625075,0.009788212,0.019141393,-0.027515752,0.044808257,0.010223243,0.036107626,0.01245278,-0.01816257,0.02229537,-0.017183749,0.011147685,-0.010060106,0.019793939,-0.018815119,0.010603896,-0.019902697,-0.010005727,-0.032627374,0.023056677,0.019685183,-0.07439041,-0.012344022,0.007721812,0.008700632,0.021425309,-0.016857475,-0.04219807,0.009135664,-0.008646253,-0.010277622,0.014899833,-0.0783057,0.019467667,0.030234698,-0.006797369,-0.034149982,-0.05155125,0.039805394,-0.014138528,0.059599333,-0.064819716,-0.026428172,-0.026210656,-0.053073857,0.031539794,0.037630238,-0.037195206,0.03697769,0.017401265,0.016966233,0.005492274,-0.08874645,0.066124804,0.004214369,0.08700632,-0.05611908,0.039370365,-0.041763037,0.011473959,0.000686534,0.009951348,-0.03066973,-0.06960506,-0.035672594,0.02599314,0.02958215,0.019576425,0.02283916,0.05394392,0.055249017,0.08222098,0.014791075,0.00625358,0.021642823,-0.005492274,-0.023817983,-0.007232401,-0.005410706,-0.019032635,0.012561538,0.04154552,0.018923877,-0.006199201,-0.007558675,-0.02131655,0.048941057,-0.014247286,0.005900117,-0.02588438,0.024470529,0.02077276,-0.03741272,-0.042415585,-0.018271329,-0.000866665,0.004377506,-0.000666142,-0.01925015,0.00570979,-0.033062402,-0.011854612,-0.03697769,0.068299964,0.013322844,-0.022621645,0.016531201,-0.003371495,0.004350316,0.004024043,0.020990277,0.013649118,-0.010440759,0.001889669,-0.04959361,-0.000367058,0.03371495,0.002188753,0.05372641,-0.011745854,0.035672594,0.0783057,0.024035498,0.005030053,0.028277056,-0.017074991,-0.011147685,0.016422443,-0.04219807,0.037630238,0.001971237,-0.08265601,-0.026536928,0.06568977,0.035020046,0.009733833,-0.030234698,-0.0010332,0.023600467,-0.025340592,0.014791075,-0.034367498,-0.055031504,-0.031974826,0.04633087,-0.020120213,-0.028929604,0.017618781,0.033062402,-0.026319413,0.001169148,0.04306813,0.024796804,-0.018923877,0.037195206,-0.023709225,-0.020664003,-0.034367498,-0.025231836,-0.027298236,-0.014464802,0.019141393,0.081350915,-0.033062402,0.04459074,-0.000666142,0.07656557,-0.008265601,-0.048070997,0.03632514,0.001026403,-0.023056677,0.008972527,0.029364634,0.03915285,0.012289644,-0.036760174,-0.019467667,-0.023165435,-0.006933317,-0.005247569,-0.02131655,0.041328005,0.03066973,-0.007395538,-0.014138528,-0.03175731,-0.004730969,-0.044808257,-0.049158577,0.041328005,0.012344022,0.00880939,-0.030017182,-0.041763037,-0.013322844,0.016422443,0.000628757,-0.04111049,0.00467659,-0.003806527,0.031104762,-0.01870636,0.066124804,0.017510023,-0.013322844,-0.017183749,-0.005002864,0.005519464,0.07265028,-0.031539794,-0.021099035,0.008863769,0.020011455,-0.000815684,0.031322278,0.016966233,0.056989145,-0.044373225,0.002079995,-0.05785921,-0.038935333,-0.008700632,-0.015008591,-0.028929604,-0.011528338,0.035020046,-0.001529408,0.000621959,0.039587878,0.028712088,0.034585014,0.00364339,0.0770006,0.024796804,-0.022404129,-0.017183749,-0.0385003,-0.02805954,0.008918148,0.027406992,0.011745854,-0.0467659,-0.02077276,-0.002460648,-0.006008875,0.048723545,0.025666868,-0.020011455,0.00831998,0.015334865,-0.012235264,-0.011365201,0.04741845,0.020664003,-0.019576425,0.009244422,0.005247569,0.008211222,-0.042850617,-0.000846273,0.018053813,0.034585014,0.07265028,-0.013377222,-0.023600467,-0.025123077,-0.035672594,-0.002365485,0.006008875,0.033497434,-0.03066973,-0.004894106,0.02501432,-0.018271329,-0.026645688,0.016204929,0.022404129,0.008591875,0.048941057,0.041980553,0.060034364,0.00116235,-0.02958215,0.023056677,0.018923877,0.04459074,0.020011455,0.08048085,0.016639959,0.006634232,0.032627374,-0.025775624,-0.06351462,-0.016422443,-0.035020046,-0.010440759,0.011637096,-0.045895837,-0.001114769,0.052638825,-0.007069264,0.000409542,0.003045222,-0.006607043,-0.005329138,0.000322875,0.022404129,0.001529408,-0.029364634,-0.010005727,-0.07221525,0.050463673,-0.06394965,-0.006634232,0.016748717,-0.014356044,-0.05785921,-0.032844886,-0.05176876,-0.03806527,0.008156843,0.059599333,-0.003996853,-0.015117349,0.040892974,0.014682317,0.053073857,0.001107971,-0.041763037,0.008483117,0.026319413,-0.015769897,0.031322278,-0.000740913,0.019902697,0.021969097,0.02588438,0.015769897,-0.053291377,-0.040675458,-0.046983417,0.050463673,-0.016857475,0.008863769,-0.020664003,-0.060469396,-0.016966233,0.026754444,0.050898705,0.042415585,0.004214369,-0.02283916,-0.004486264,-0.00935318,0.008374359,-0.066124804,0.010603896,-0.003303522,-0.02914712,0.003996853,-0.033932466,-0.012398402,0.013268464,-0.04741845,-0.01816257,-0.012344022,0.003915285,-0.033932466,0.040240426,-0.07482544,0.004758159,-0.002528621,-0.041763037,0.05785921,-0.005138811,-0.003317116,0.004459074,0.006389527,-0.000186928,-0.026428172,0.06699487,-0.002718948,-0.028277056,0.02283916,0.008646253,-0.023709225,-0.03741272,0.07482544,0.028494572,0.023165435,-0.011256443,-0.07134519,0.023817983,-0.004948485,0.009407559,-0.05785921,-0.05002864,-0.004866917,-0.006960506,-0.01141958,-0.03915285,-0.020446487,0.007558675,-0.017727539,0.037630238,-0.00880939,-0.022730403,0.003317116,-0.023491709,-0.00170614,0.015661139,-0.062209524,0.026863204,-0.002365485,-0.022947919,-0.026754444,-4.6307e-05,0.006579853,0.010114485,-0.009407559,-0.013268464,0.038935333,0.009951348,-0.021099035,-0.00831998,0.002583,0.029364634,0.006498285,-0.04350316,0.033279918,-0.004214369,-0.020881519,-0.024253013,-0.00674299,-0.01245278,0.035672594,-0.042850617,-0.06525475,-0.005900117,0.036107626,-0.004948485,-0.008156843,-0.005682601,0.053073857,0.015769897,0.003480253,-0.07787067,0.004948485,-0.000353463,-0.002379079,-0.044373225,-0.004024043,0.055031504,0.010603896,0.033279918,-0.031974826,0.050246153,-0.026101897,0.037195206,-0.050246153,-0.060034364,-0.009190043,0.031539794,0.012344022,-0.019902697,-0.010712654,-0.035455078,0.018488845,0.029799666,-0.04785348,-0.007776191,0.019358909,0.009951348,0.005900117,0.008156843,0.04959361,0.015334865,-0.064819716,0.004295937,1.8056e-05,0.025775624,0.0028413,-0.010658275,-0.03066973,0.004214369,0.04959361,-0.01870636,-0.02338295,-0.020337729,0.024035498,0.024035498,0.008483117,0.01870636,0.050246153,0.000856469,0.050898705,-0.029364634,0.038282786,-0.031104762,-0.045460805,-0.036760174,-0.041328005,0.03480253,0.007558675,0.008483117,0.018380087,-0.006008875,-0.020120213,0.017618781,0.06133946,-0.03632514,-0.006634232,0.012235264,0.013921012,0.05785921,0.00880939,0.001821695,0.015443623,0.00315398,-0.04154552,0.010549517,0.005818548,0.007232401,-0.004567832,0.050681185,0.016748717,-0.012017749,0.015117349,-0.047200933,-0.011528338,0.004078422,0.031322278,0.02708072,-0.039805394,0.012344022,-0.011365201]'::vector, now()
WHERE NOT EXISTS (SELECT 1 FROM "KnowledgeDocuments" WHERE "Title"='How to prepare for a specialist visit');

