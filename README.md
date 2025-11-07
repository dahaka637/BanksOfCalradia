
# Banks of Calradia

![Banks of Calradia Banner](https://github.com/user-attachments/assets/782e2675-fba9-46bd-ae41-e4a3f9ef1c15)

### Advanced Banking & Economic Simulation for Mount & Blade II: Bannerlord

**Banks of Calradia** introduces a complete and realistic banking system to *Mount & Blade II: Bannerlord*, seamlessly integrated into the campaign economy.

---

## 💡 Overview

A modular, fully localized system that adds banks to every settlement. Manage your wealth, take loans, and influence prosperity through finance.

**Core Features**
- **Savings Accounts** — Earn interest based on city prosperity and security.  
- **Loans** — Borrow money with contracts, terms, and interest penalties.  
- **Prosperity Impact** — Deposits dynamically influence settlement growth.  
- **Trade XP** — Gain passive Trade experience from daily profits.  
- **Dynamic Economy** — Realistic interest and risk curves balance gameplay.  

---

## ⚙️ Systems & Structure

| Module | Description |
|--------|--------------|
| **Finance Processor** | Integrates savings and loan previews into clan finance UI. |
| **Prosperity Model** | Converts stored wealth into settlement prosperity gains. |
| **Loan Processor** | Handles daily payments, penalties, and debt caps. |
| **Bank Behavior** | Core system linking savings, loans, and XP generation. |
| **Localization Helper (L)** | Safe multilingual text access (EN, BR, SP, DE, RU). |

---

## 🧮 Economic Logic

- **Interest Curves** — Calibrated formulas scale rates by prosperity.  
- **Withdrawal Fees** — Dynamic curve based on loyalty & security.  
- **Trade XP** — Smooth logarithmic scaling prevents late-game exploits.  
- **Loan Risk** — Debt caps and freeze logic ensure fair contracts.  
- **Prosperity Feedback** — Poor towns grow faster from investments.  

---

## 🗂 Directory Overview

```
BanksOfCalradia/
├── Source/
│   ├── Core/                  # Utilities, localization, formatting
│   ├── Systems/               # Economic logic and campaign behaviors
│   ├── UI/                    # Savings, loans, and payments menus
│   └── SubModule.cs           # Initialization entry point
├── ModuleData/Languages/      # Multilingual localization (EN, BR, SP, DE, RU)
├── SubModule.xml              # Bannerlord module descriptor
└── BanksOfCalradia.csproj     # Build configuration
```

---

## 🌍 Localization

Fully multilingual with fallback safety via helper `L`:

- **English (EN)** — Default  
- **Portuguese (BR)**  
- **Spanish (SP)**  
- **German (DE)**  
- **Russian (RU)**  

---

## 🧱 Technical Notes

- Compatible with **Bannerlord v1.3.x+**  
- Uses reflection-safe model injection for cross-mod stability  
- Follows clean modular architecture (`FinanceProcessor`, `LoanProcessor`, etc.)  
- Licensed under **MIT** — open source and mod-friendly  

---

## 👤 Author

**Dahaka** — Creator & Developer  
📦 [GitHub Repository](https://github.com/dahaka637/BanksOfCalradia)  
💬 [NexusMods Page](https://www.nexusmods.com/mountandblade2bannerlord/mods/)  

---

> “Banks of Calradia brings economic life to Calradia — where your gold truly shapes the world.”
