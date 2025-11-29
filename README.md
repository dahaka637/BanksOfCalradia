# Banks of Calradia

Advanced banking + economic simulation for Mount & Blade II: Bannerlord.

Banks of Calradia adds a per-settlement banking layer with savings accounts, loans (contracts, installments, late fees), prosperity impact, and passive Trade XP. All balances and contracts persist through saves via an internal storage layer.

## Features (Player-Facing)

- Savings accounts in each town
  - Daily interest driven by settlement economy (calibrated to avoid rounding/micro-deposit exploits).
  - Withdrawals with dynamic fees driven by settlement risk (prosperity/loyalty/security signals).
- Loans and credit
  - Credit limit and terms based on settlement prosperity + clan renown.
  - Real contracts, installments, late penalties, partial payments, and early-payment benefit.
  - Debt caps / safety logic to avoid runaway snowballing.
- Economy integration
  - Deposits contribute to the settlement prosperity forecast.
  - Banking profits can grant passive Trade XP (damped scaling).

## Compatibility

- Bannerlord: v1.3.7
- DLC: War Sails (supported)
- Dependencies: none

## Installation

### Steam Workshop
Subscribe and enable **Banks of Calradia** in the launcher.

### Manual
1. Copy the module folder into:
   `Mount & Blade II Bannerlord/Modules/BanksOfCalradia/`
2. Enable the mod in the launcher.

## Technical Architecture (Current Codebase)

High-level responsibility split:

- `Source/Core/`
  - `BankUtils.cs`: currency/percent formatting + shared helpers/caps.
  - `FinanceLedger.cs`: internal ledger for tracing banking operations (debug/audit).
  - `LocalizationHelper.cs`: safe `L` helper for native `TextObject` localization with fallbacks.
- `Source/Systems/`
  - `BankCampaignBehavior.cs`: central coordinator; hooks the campaign lifecycle, registers menus, runs daily processing.
  - `BankStorage.cs`: persistent data layer; stores savings + loan contracts keyed by player/town IDs.
  - `Data/`
    - `LoanContractData.cs`, `SavingsAccountData.cs`: structured persistent models.
  - `Processing/`
    - `FinanceProcessor.cs`: integrates bank income/preview into finance UI/expected gold change.
    - `LoanProcessor.cs`: daily loan logic (installments, late fees, caps, completion).
    - `ProsperityModel.cs`: savings → expected prosperity change injection.
    - `BankFoodModelProxy.cs`: UI-facing proxy layer for food/prosperity presentation (non-destructive).
    - `BankFinanceFallbackBehavior.cs`: safe fallback hooks for version/edge cases.
  - `Utils/`
    - `BankSuccessionUtils.cs`: transfers banking data across hero/clan succession.
    - `BankTradeXpUtils.cs`: converts profits to Trade XP with damping.
- `Source/UI/`
  - `BankSafeUI.cs`: UI-safety layer (context checks + safe switching + race-condition protection).
  - `Menu_Savings.cs`: deposit/withdraw UI, interest/fee previews.
  - `Menu_Loan.cs`: credit simulation + loan creation UI.
  - `Menu_LoanPayments.cs`: loan listing/detail/payment UI.
- `ModuleData/Languages/`
  - `EN/`, `BR/`, `SP/`, `DE/`, `RU/`: native Bannerlord localization XML packs (`bc_strings.xml`, `language_data.xml`).
- Entry points / build:
  - `SubModule.cs`: module initializer.
  - `SubModule.xml`: module descriptor.
  - `BanksOfCalradia.csproj`, `Directory.Build.props`: build rules and compilation settings.

## Localization Notes

The project uses Bannerlord’s native XML localization. Keys are stored in `bc_strings.xml` per language, and accessed in code via:

- `L.T(id, fallback)` → `TextObject`
- `L.S(id, fallback)` → `string`

This guarantees stable fallbacks even if a translation key is missing.

## Build (Developers)

- Build the project (`BanksOfCalradia.csproj`) in Release.
- Output should generate `BanksOfCalradia.dll` for the module `bin/` output expected by Bannerlord.
- Keep `SubModule.xml` and language files under `ModuleData/` alongside the compiled DLL.

Repository (source + issue tracker):
https://github.com/dahaka637/BanksOfCalradia

## Troubleshooting

If you see a crash when opening bank menus:
- Ensure you are inside a real town scene (bank menus validate settlement context).
- Try waiting a moment after loading/entering a town before opening the bank (some UI contexts initialize asynchronously).
- If it persists, report:
  - Bannerlord version
  - Mod version
  - Repro steps
  - Crash stack trace / log excerpt

## License
MIT (see repository for details).
