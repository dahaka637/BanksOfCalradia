<img width="1600" height="750" alt="Banks of Calradia" src="https://github.com/user-attachments/assets/782e2675-fba9-46bd-ae41-e4a3f9ef1c15" />

# Banks of Calradia

### Advanced Banking & Economic Simulation for Mount & Blade II: Bannerlord

**Banks of Calradia** is an advanced economic simulation mod for *Mount & Blade II: Bannerlord*, introducing a fully functional and realistic banking system across Calradia.

Players can:

- **Open savings accounts** and earn dynamic interest.
- **Take loans** with contracts, installments, and penalties.
- **Influence settlement prosperity** with stored wealth.
- **Gain Trade XP passively** from financial activity.

The entire system is driven by calibrated curves and dynamic formulas tied to prosperity, loyalty, security, and clan renown — so the economy reacts to you and evolves with the campaign.

---

## ⚙️ Core Concept

Banks are available in every major settlement and offer a complete financial layer on top of Bannerlord’s native economy:

- **Savings Accounts**
  - Earn daily interest based on city prosperity and stability.
  - Interest uses a calibrated curve to avoid exploits and preserve realism.
- **Loans**
  - Take out configurable loans with real contracts, term, and interest.
  - Daily installments, partial payments, and late fees.
  - Debt caps and freeze logic prevent infinite runaway interest.
- **Prosperity Impact**
  - Your deposits feed directly into a settlement’s prosperity forecast.
  - Poor cities benefit more from injections of capital; extremely rich cities are softly penalized.
- **Passive Trade XP**
  - Daily profits from savings generate Trade experience for your clan leader.
  - XP is scaled with damping so late-game empires don’t break progression.

Every calculation is fully dynamic and takes into account settlement prosperity, loyalty, security, and the player’s renown — ensuring a balanced, immersive simulation.

---

## 🧩 Technical Structure (High-Level)

The mod is designed in layers for clarity, maintainability, and future scalability:

| Layer        | Path                     | Purpose                                                     |
|-------------|--------------------------|-------------------------------------------------------------|
| **Core**    | `Source/Core/`           | Shared utilities, localization helpers, and formatting.     |
| **Systems** | `Source/Systems/`        | Savings, loans, prosperity, XP, and campaign behaviors.     |
| **UI**      | `Source/UI/`             | In-game menu logic (savings, loans, payments).              |
| **Data**    | `Source/Systems/Data/`   | Structured models for savings and loan contracts.           |
| **Module**  | `ModuleData/`            | Localization XML and module descriptors.                    |

---

## 💰 Dynamic Economy System

All financial systems are reactive and interconnected:

- **Prosperity, Loyalty & Security**
  - Influence withdrawal fees, interest rates, and overall banking risk.
- **Renown & Reputation**
  - Modify credit limits and loan terms for the player.
- **Withdrawal Fees**
  - Scale with city stability: higher instability → higher fees.
- **Deposits → Prosperity**
  - Wealth stored in banks increases settlement prosperity over time.
- **Loans → Real Risk**
  - Late payments apply fees and debt caps to avoid soft-locking the player.
- **Trade XP**
  - Calculated from profits using a soft scaling function to avoid XP inflation.

The result is a believable financial landscape where careful planning pays off — and reckless debt has consequences.

---

## 🧠 Key Algorithms

- **Calibrated Interest Curve**
  - Curved savings interest that rewards investment, but prevents exponential snowballing.
- **Loan Forecasting**
  - Calculates credit limits and installments based on prosperity and clan renown.
- **Dynamic Prosperity Forecast**
  - Savings in each settlement contribute directly to its prosperity change.
- **Trade XP Logic**
  - Converts daily banking profits into Trade skill XP, with logarithmic dampening.

---

# 📂 Directory Structure (Detailed Overview)

```text
BanksOfCalradia/
├── ModuleData/
│   └── Languages/
│       ├── BR/
│       │   ├── bc_strings.xml        # Portuguese (BR) translations for all UI/system messages
│       │   └── language_data.xml     # Registers the BR localization package
│       ├── EN/
│       │   ├── bc_strings.xml        # English base localization (fallback language)
│       │   └── language_data.xml     # Registers the EN localization package
│       ├── SP/
│       │   ├── bc_strings.xml        # Spanish (LA) translations for UI and notifications
│       │   └── language_data.xml     # Registers the SP localization package
│
├── Source/
│   ├── Core/
│   │   ├── BankUtils.cs              # Currency/rate helpers and standard formatting
│   │   ├── FinanceLedger.cs          # In-memory transaction ledger for audits
│   │   └── LocalizationHelper.cs     # L helper for safe multilingual access
│   │
│   ├── Systems/
│   │   ├── Data/
│   │   │   ├── LoanContractData.cs   # Data model for loan contracts
│   │   │   └── SavingsAccountData.cs # Data model for savings accounts
│   │   │
│   │   ├── Processing/
│   │   │   ├── FinanceProcessor.cs   # Injects bank income into Bannerlord’s finance view
│   │   │   ├── LoanProcessor.cs      # Daily loan processing, payments, and penalties
│   │   │   └── ProsperityModel.cs    # Savings → settlement prosperity link
│   │   │
│   │   ├── Utils/
│   │   │   ├── BankSuccessionUtils.cs# Transfers accounts on hero/clan succession
│   │   │   └── BankTradeXpUtils.cs   # Converts banking profits into Trade XP
│   │   │
│   │   ├── BankCampaignBehavior.cs   # Central campaign system linking all modules
│   │   └── BankStorage.cs            # Persistent data layer for accounts and loans
│   │
│   ├── UI/
│   │   ├── Menu_Loan.cs              # Interface for loan creation & credit simulation
│   │   ├── Menu_LoanPayments.cs      # Interface for viewing/repaying active loans
│   │   └── Menu_Savings.cs           # Interface for deposits/withdrawals & savings overview
│   │
│   └── SubModule.cs                  # Initializes systems and hooks into campaign lifecycle
│
├── BanksOfCalradia.csproj            # Project definition and compilation rules
├── Directory.Build.props             # Shared build configuration
├── SubModule.xml                     # Bannerlord module descriptor
└── README.md                         # Project documentation (this file)
```

---

## 🧱 Main Components

### 🧩 Directory.Build.props
**Function:** Global build configuration file for the project.  
**Description:** Defines shared MSBuild properties and ensures consistent compilation across all source folders.  
**How it works:** Applies common compiler and build settings to all C# projects in the repository.

---

### 🧩 BanksOfCalradia.csproj
**Function:** Core project definition for the mod.  
**Description:** Specifies compilation targets, dependencies, and build outputs for the main C# assembly.  
**How it works:** Instructs MSBuild to compile `BanksOfCalradia.dll` from the `Source/` tree with appropriate references and configuration.

---

### 🧩 SubModule.xml
**Function:** Bannerlord module descriptor.  
**Description:** Declares the mod’s identity, version, dependencies, and main entry point.  
**How it works:** When Bannerlord starts, this XML is read by the module loader to register *Banks of Calradia* as a singleplayer module, loading `BanksOfCalradia.dll` and ensuring compatibility with Native/Sandbox/StoryMode.

---

### 🧩 SubModule.cs
**Function:** Core initializer of the mod.  
**Description:** Registers systems, models, and in-game menus when the module loads.  
**How it works:** On campaign start, it initializes:
- `BankCampaignBehavior` (central coordinator),
- economic models (`FinanceProcessor`, `ProsperityModel`),
- and UI components (`Menu_Savings`, `Menu_Loan`, `Menu_LoanPayments`).

---

### 🧩 Menu_Loan.cs
**Function:** In-game loan creation interface.  
**Description:** Lets the player request new loans from a city’s bank, previewing credit limits, interest rates, and duration.  
**How it works:** Displays a loan menu tied to the current settlement, runs the loan forecasting algorithm using clan renown and prosperity, and creates `LoanContractData` entries when confirmed.

---

### 🧩 Menu_LoanPayments.cs
**Function:** Loan management and repayment interface.  
**Description:** Shows active loans, remaining balances, and allows partial or full payments.  
**How it works:** Reads from `BankStorage`, uses early-repayment logic (based on total interest and remaining term), and applies payments and discounts safely with in-game feedback.

---

### 🧩 Menu_Savings.cs
**Function:** Savings and withdrawal interaction.  
**Description:** Provides UI for deposits, withdrawals, and interest previews per settlement.  
**How it works:**
- Uses the calibrated interest curve to estimate daily/annual returns.
- Calculates withdrawal fees using a “Reversed Risk Curve” based on prosperity, security, and loyalty.
- Synchronizes results with `BankCampaignBehavior` and `BankStorage`.

---

### 🧩 BankCampaignBehavior.cs
**Function:** Central campaign behavior.  
**Description:** Connects Bannerlord’s campaign lifecycle with all banking systems.  
**How it works:**
- Registers menus and hooks into daily tick events.
- Coordinates savings/loan processing and Trade XP.
- Handles save/load by delegating to `BankStorage`.

---

### 🧩 BankStorage.cs
**Function:** Persistent data layer for all banking operations.  
**Description:** Stores player savings accounts and loan contracts in structured collections.  
**How it works:**
- Uses dictionaries keyed by `PlayerId` and `TownId`.
- Provides methods to create, fetch, and remove accounts/contracts.
- Supports daily updates and cleanup through processors.

---

### 🧩 BankSuccessionUtils.cs
**Function:** Maintains ownership consistency after hero/clan changes.  
**Description:** Ensures that when the player character or clan leader changes, all banking data follows correctly.  
**How it works:** Merges and transfers accounts and loans tied to outdated hero IDs into the current player hero, preventing data loss on succession.

---

### 🧩 BankTradeXpUtils.cs
**Function:** Converts banking profits into Trade XP.  
**Description:** Generates Trade experience from daily financial gains without requiring direct trading.  
**How it works:**
- Aggregates daily interest across all savings.
- Applies logarithmic damping to avoid XP explosions for huge deposits.
- Grants XP directly to the main hero’s Trade skill each in-game day.

---

### 🧩 LoanProcessor.cs
**Function:** Automates daily loan behavior.  
**Description:** Manages payments, late fees, caps, and contract completion.  
**How it works:**
- Runs via `CampaignEvents.DailyTickClanEvent`.
- Calculates installments based on remaining balance and term.
- Applies late fees with a 10× contracted total cap to stop infinite stacking.
- Removes contracts when fully paid and logs localized notifications.

---

### 🧩 ProsperityModel.cs
**Function:** Extends Bannerlord’s settlement prosperity model.  
**Description:** Links stored wealth to a settlement’s expected prosperity change.  
**How it works:**
- Reads savings per town from `BankStorage`.
- Uses a calibrated prosperity curve that:
  - boosts poor cities more,
  - soft-penalizes extremely rich cities.
- Injects the daily prosperity forecast into the standard model safely.

---

### 🧩 FinanceProcessor.cs
**Function:** Integrates banking into the clan finance panel.  
**Description:** Adds bank-related income and loan previews into the Expected Gold Change UI.  
**How it works:**
- Calculates daily interest per settlement and adds it as income.
- Optionally shows loan installment previews in the detailed (ALT) view.
- Uses the `L` helper for fully localized descriptions.

---

### 🧩 LoanContractData.cs
**Function:** Data structure for loan contracts.  
**Description:** Represents each loan’s principal, interest, remaining balance, and term.  
**How it works:**  
Used by `LoanProcessor` and UI menus to track contract status and compute payments.

---

### 🧩 SavingsAccountData.cs
**Function:** Data structure for savings accounts.  
**Description:** Tracks player deposits and pending fractional interest per town.  
**How it works:**  
Provides the core data used by `FinanceProcessor` and `ProsperityModel` to compute daily interest and prosperity impact.

---

### 🧩 BankUtils.cs
**Function:** Shared math/formatting utilities.  
**Description:** Normalizes rates, caps, and formats currency/percentages.  
**How it works:**  
Ensures consistent financial logic and visuals across all systems.

---

### 🧩 FinanceLedger.cs
**Function:** Internal ledger for banking activity.  
**Description:** Tracks deposits, withdrawals, interest, and adjustments for debugging and audits.  
**How it works:**  
Offers query and rollback tools useful during development or balancing.

---

### 🧩 LocalizationHelper.cs
**Function:** Simple access to localized text.  
**Description:** Provides the static helper `L` for `TextObject` and string retrieval.  
**How it works:**
- `L.T(id, fallback)` → `TextObject` with key `bank_{id}`.
- `L.S(id, fallback)` → string using the same key.
- Guarantees safe fallbacks when translations are missing.

---

### 🧩 Localization Files (`bc_strings.xml` & `language_data.xml`)
**Function:** Full multilingual support.  
**Description:** Provide translations for all UI labels, notifications, and logs.  
**How it works:**
- `language_data.xml` registers each language.
- `bc_strings.xml` stores all `bank_*` entries.
- Integrated directly with `LocalizationHelper`.

---

## 🌐 Localization

Full multilingual support using Bannerlord’s native XML system and the `L` helper.

Available languages:

- **English** (EN) — default fallback
- **Português (Brasil)** (BR)
- **Español** (SP)

Files live under:

- `ModuleData/Languages/EN/`
- `ModuleData/Languages/BR/`
- `ModuleData/Languages/SP/`

---

## 👨‍💻 Author & License

- **Author:** Dahaka  
- **Version:** 1.0.6.0 (Stable Release)  
- **License:** MIT — free to modify and redistribute with attribution.

---

## 🔚 Summary

**Banks of Calradia** introduces a complete financial layer to Bannerlord’s economy — bringing savings, loans, prosperity, and Trade XP together into a coherent system.

The modular codebase keeps things clean and maintainable, while the calibrated curves and daily processors ensure that the economy feels alive, fair, and reactive to the player’s financial decisions.
