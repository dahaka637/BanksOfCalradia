# Banks of Calradia
### Technical Documentation — Version 2.6.0

## Overview
**Banks of Calradia** is a financial simulation mod for *Mount & Blade II: Bannerlord* that introduces a fully functional banking system with realistic savings, loan contracts, and economic interactions tied to the prosperity of each settlement.  
The project is written in **C#** and designed for maintainability, modular expansion, and native localization support.

---

## 1. Architecture Overview

### 1.1 Core Modules
The mod is divided into structured components under `Source/`, each responsible for a specific layer of logic:

| Layer | Path | Responsibility |
|--------|------|----------------|
| **Core** | `/Source/Core/` | Fundamental helpers such as utilities, ledger management, and localization wrapper. |
| **Systems** | `/Source/Systems/` | Persistent data models, processing logic, campaign behaviors, and auto-save routines. |
| **UI** | `/Source/UI/` | In-game menu definitions (savings, loan, loan payments). |
| **ModuleData** | `/ModuleData/` | XML language files for localization (BR, EN, SP). |

### 1.2 Entry Point
`SubModule.cs` initializes the entire system when the campaign starts. It registers:
- Core behaviors: `BankCampaignBehavior`
- Models: `FinanceProcessor`, `BankLoanProcessor`, `BankProsperityModel`
- Menus: `BankMenu_Savings`, `BankMenu_Loan`, `BankMenu_LoanPay`

---

## 2. Core Components

### 2.1 `BankCampaignBehavior`
Manages:
- Bank data persistence (JSON serialization in save files)
- Event registration (`OnSessionLaunched`, `DailyTick`)
- Dynamic menu registration for towns
- Daily XP rewards from interest earnings

It integrates with the campaign system to display the “Visit Bank” option in towns and synchronize financial data across saves.

### 2.2 `BankStorage`
Central persistence model holding all player financial data.
```csharp
Dictionary<string, List<BankSavingsData>> SavingsByPlayer;
Dictionary<string, List<BankLoanData>> LoansByPlayer;
```
Handles creation, lookup, and deletion of loan/savings entries. Includes daily loan updates with late fee penalties and safety caps.

### 2.3 `FinanceProcessor` and Models
Implements financial behavior at campaign tick level (e.g., interest accumulation).  
`ProsperityModel` adjusts interest rates dynamically based on city prosperity, loyalty, and security factors.

---

## 3. Data Models

| Class | Description |
|--------|--------------|
| **`BankLoanData`** | Stores loan contracts — principal, interest, duration, late fee, and identifiers. |
| **`BankSavingsData`** | Stores savings account values and pending interest fragments. |
| **`LoanContractData`** | Abstraction for potential loan processing extensions. |
| **`SavingsAccountData`** | Placeholder for detailed savings operations (future-proofed). |

---

## 4. UI and Interaction Layer

### 4.1 `BankMenu_Savings`
Implements savings operations with deposit/withdraw logic.  
Withdraw fees are dynamically calculated from town economic factors (prosperity, security, loyalty).  
Includes quick-access options, custom input dialogs, and transaction confirmations.

### 4.2 `BankMenu_Loan`
Handles loan simulation and contract creation:
- Calculates max credit using player renown + town prosperity.
- Dynamically adjusts interest and late fees.
- Generates loan contracts serialized into storage.

### 4.3 `BankMenu_LoanPay`
Allows users to manage and pay existing loans:
- Displays active loans filtered per town.
- Supports partial payments.
- Includes inquiry dialogs for selection and input validation.

---

## 5. Localization System

The mod uses a **custom localization helper** (`LocalizationHelper.cs`) integrated with native Bannerlord `TextObject`.

- `L.S(id, fallback)` → returns localized text string.
- `L.T(id, fallback)` → returns `TextObject` with variables.

### Directory Structure
```
ModuleData/
└── Languages/
    ├── BR/
    │   ├── bc_strings.xml
    │   ├── language_data.xml
    ├── EN/
    │   ├── bc_strings.xml
    │   ├── language_data.xml
    ├── SP/
        ├── bc_strings.xml
        ├── language_data.xml
```

The English language acts as the fallback embedded in code, while BR and SP use XML localization.

---

## 6. Persistence and Data Handling

- All financial data is stored within the campaign save file as JSON (`Bank.StorageJson`).
- `BankStorage` serializes safely via Newtonsoft.Json with null protection.
- Legacy data compatibility is maintained for older save structures.

---

## 7. Economic Simulation Logic

### 7.1 Savings Interest
Interest rate per city depends on:
```
AnnualInterest = Prosperity / 400
DailyInterest = AnnualInterest / 120
```
This ensures realistic growth tied to in-game economy.

### 7.2 Loan Credit System
Loan parameters depend on:
- **Prosperity** (affects available credit and lower interest)
- **Renown** (increases credit and reduces late fees)
- **Installments** (higher terms slightly increase total interest)

Capped and smoothed with logarithmic factors to avoid exploits or runaway growth.

---

## 8. Extensibility and Integration

The system is designed for modular growth:
- Future support for **city treasuries**, **investments**, or **interest rate events**.
- Possible integration with third-party mods via public `BankStorage` accessors.
- Each menu and behavior can be registered independently.

---

## 9. Development Guidelines

### 9.1 Code Style
- Follows C# 10 conventions with clear naming and region markers.
- Comments are bilingual (English logic, Portuguese explanations).

### 9.2 Build Notes
- Target Framework: `.NET Framework 4.7.2` (Bannerlord compatibility).
- Output: DLL placed in `/bin/Win64_Shipping_Client/Modules/BanksOfCalradia/`.
- Requires `Newtonsoft.Json` and TaleWorlds libraries from Bannerlord SDK.

### 9.3 Safe Compilation
Ensure references are properly configured to the game’s binaries:
```
Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\
Mount & Blade II Bannerlord\Modules\Native\
Mount & Blade II Bannerlord\Modules\SandBox\
```

---

## 10. Authors and Credits

**Author:** Henrique “Dahaka” Wegher  
**Version:** 2.6.0 — Localization + Stability overhaul  
**License:** MIT (optional open distribution)  

---

## 11. Repository Structure Summary
```
BanksOfCalradia/
├── Source/
│   ├── Core/
│   ├── Systems/
│   ├── UI/
│   ├── SubModule.cs
├── ModuleData/
│   ├── Languages/
│   │   ├── BR/
│   │   ├── EN/
│   │   ├── SP/
├── BanksOfCalradia.csproj
├── Directory.Build.props
├── SubModule.xml
└── README.md
```

---

## 12. Summary

Banks of Calradia introduces a deep, realistic financial framework into Bannerlord — balancing player risk, economic simulation, and in-game rewards.  
Its modular design allows developers to extend or adapt the system without compromising stability or compatibility.
