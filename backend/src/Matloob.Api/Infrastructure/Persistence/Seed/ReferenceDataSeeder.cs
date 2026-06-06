using System.Reflection;
using System.Text.Json;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent reference-data seeder. Inserts the canonical lookup values used
/// by the public frontend (<c>/api/v1/init-data</c>) into each table when the
/// table is empty; never touches existing rows.
///
/// Equivalent to the legacy Laravel <c>DatabaseSeeder</c> for reference data.
/// Auto-increment ids and the <c>ajeer_id</c> columns are dropped on purpose —
/// the new system uses UUIDs and Ajeer is out of scope for the migration.
///
/// JSON files for the largest lookups (nationalities, job titles, offer
/// cancellation / rejection reasons) ship as embedded resources next to this
/// class so the seeder runs the same way in Dev, integration tests, and a
/// published build.
///
/// <para>
/// <b>Phase OAO-1 coverage check.</b> The Opportunities / Applicants / Offers
/// / Evaluations slices depend on three lookup tables that this seeder owns:
/// <list type="bullet">
///   <item><c>opportunity_categories</c> — seven parent groups with their
///     children, populated via <see cref="SeedOpportunityCategoriesAsync"/>.
///     ForVacancy / IsOther flags drive the worker-vs-establishment routing
///     in the browse endpoints.</item>
///   <item><c>offer_rejection_reasons</c> — loaded from
///     <c>rejection_reasons.json</c> via <see cref="NamedAjeerItem"/>. The
///     <c>ajeer_id</c> field is parsed but never written; per
///     docs/25-ajeer-disposition.md the new system does not retain that
///     foreign key.</item>
///   <item><c>offer_cancellation_reasons</c> — same shape as the rejection
///     reasons.</item>
/// </list>
/// No additional seed work is needed for Phase OAO-1; the existing data is
/// complete and Ajeer-stripped.
/// </para>
/// </summary>
public static class ReferenceDataSeeder
{
    private const string ResourceNamespace =
        "Matloob.Api.Infrastructure.Persistence.Seed.Data";

    /// <summary>
    /// Apply every per-entity seed in a single SaveChanges so an empty database
    /// becomes usable after one call. Safe to invoke repeatedly: each per-entity
    /// step checks for existing rows first.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await SeedCitiesAsync(db, ct);
        await SeedRegionsAsync(db, ct);
        // Persist base regions + cities first so the geography linker can resolve
        // them by name (it reads from the DB, not the pending change tracker).
        await db.SaveChangesAsync(ct);
        await SeedSaudiGeographyAsync(db, ct);
        await SeedLanguagesAsync(db, ct);
        await SeedNationalitiesAsync(db, ct);
        await SeedBanksAsync(db, ct);
        await SeedJobTitlesAsync(db, ct);
        await SeedEventTypesAsync(db, ct);
        await SeedOpportunityCategoriesAsync(db, ct);
        await SeedOfferCancellationReasonsAsync(db, ct);
        await SeedOfferRejectionReasonsAsync(db, ct);
        await SeedSeasonsAsync(db, ct);
        await SeedSuggestedLocationsAsync(db, ct);
        await SeedSuggestedAttendeesAsync(db, ct);
        await SeedSettingsAsync(db, ct);
        await SeedTranslationsAsync(db, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCitiesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Cities.AnyAsync(ct))
        {
            return;
        }

        // Saudi Arabia top-21 cities — same list the legacy fallback shipped.
        string[] names =
        {
            "الرياض", "جدة", "مكة المكرمة", "المدينة المنورة", "الدمام",
            "الخبر", "الظهران", "تبوك", "بريدة", "خميس مشيط",
            "حائل", "نجران", "جازان", "أبها", "الطائف",
            "القطيف", "ينبع", "الجبيل", "عرعر", "سكاكا",
            "الباحة",
        };

        foreach (var name in names)
        {
            db.Cities.Add(new City(Guid.NewGuid(), name));
        }
    }

    private static async Task SeedRegionsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Regions.AnyAsync(ct))
        {
            return;
        }

        string[] names =
        {
            "منطقة الرياض",
            "منطقة الشرقية",
            "منطقة مكة المكرمة",
            "منطقة المدينة المنورة",
            "منطقة القصيم",
            "منطقة عسير",
            "منطقة حائل",
            "منطقة تبوك",
            "منطقة الباحة",
            "منطقة الحدود الشمالية",
            "منطقة الجوف",
            "منطقة جازان",
            "منطقة نجران",
        };

        foreach (var name in names)
        {
            db.Regions.Add(new Region(Guid.NewGuid(), name));
        }
    }

    /// <summary>
    /// Links cities to their region and seeds districts from
    /// <c>saudi_geography.json</c> — the Saudi Region → City → District
    /// hierarchy used by the establishment registration lookups. Idempotent and
    /// additive: existing cities are linked to a region only when unset, and a
    /// district is inserted only when that (city, name) pair is missing, so it
    /// is safe to re-run against a partially-seeded database.
    /// </summary>
    private static async Task SeedSaudiGeographyAsync(AppDbContext db, CancellationToken ct)
    {
        var geography = LoadJsonArray<GeographyRegion>("saudi_geography.json");

        // Tracked loads — SetRegion on an existing city must persist on save.
        var regionsByName = await db.Regions.ToDictionaryAsync(r => r.Name, r => r.Id, ct);
        var citiesByName = await db.Cities.ToDictionaryAsync(c => c.Name, c => c, ct);
        var districtKeys = (await db.Districts
                .Select(d => new { d.CityId, d.Name })
                .ToListAsync(ct))
            .Select(d => (d.CityId, d.Name))
            .ToHashSet();

        foreach (var geoRegion in geography)
        {
            if (!regionsByName.TryGetValue(geoRegion.Region, out var regionId))
            {
                // SeedRegions covers the 13 canonical regions; create any extra
                // so the hierarchy stays intact rather than dropping its cities.
                regionId = Guid.NewGuid();
                db.Regions.Add(new Region(regionId, geoRegion.Region));
                regionsByName[geoRegion.Region] = regionId;
            }

            foreach (var geoCity in geoRegion.Cities)
            {
                Guid cityId;
                if (citiesByName.TryGetValue(geoCity.Name, out var existingCity))
                {
                    if (existingCity.RegionId is null)
                    {
                        existingCity.SetRegion(regionId);
                    }
                    cityId = existingCity.Id;
                }
                else
                {
                    cityId = Guid.NewGuid();
                    var newCity = new City(cityId, geoCity.Name, regionId);
                    db.Cities.Add(newCity);
                    citiesByName[geoCity.Name] = newCity;
                }

                foreach (var districtName in geoCity.Districts)
                {
                    if (districtKeys.Add((cityId, districtName)))
                    {
                        db.Districts.Add(new District(Guid.NewGuid(), districtName, cityId));
                    }
                }
            }
        }
    }

    private static async Task SeedLanguagesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Languages.AnyAsync(ct))
        {
            return;
        }

        string[] names = { "إنجليزي", "عربي", "فرنسي", "ألماني" };

        foreach (var name in names)
        {
            db.Languages.Add(new Language(Guid.NewGuid(), name));
        }
    }

    private static async Task SeedNationalitiesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Nationalities.AnyAsync(ct))
        {
            return;
        }

        var names = LoadJsonArray<string>("nationalities.json");
        foreach (var name in names.Distinct())
        {
            db.Nationalities.Add(new Nationality(Guid.NewGuid(), name));
        }
    }

    private static async Task SeedBanksAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Banks.AnyAsync(ct))
        {
            return;
        }

        string[] names =
        {
            "البنك الأهلي التجاري",
            "البنك الأول",
            "البنك السعودي للاستثمار",
            "مصرف الإنماء",
            "البنك السعودي الفرنسي",
            "بنك الرياض",
            "مصرف الراجحي",
            "البنك العربي الوطني",
            "بنك البلاد",
            "بنك الجزيرة",
            "بنك الخليج الدولي",
        };

        foreach (var name in names)
        {
            db.Banks.Add(new Bank(Guid.NewGuid(), name));
        }
    }

    private static async Task SeedJobTitlesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.JobTitles.AnyAsync(ct))
        {
            return;
        }

        var items = LoadJsonArray<NamedAjeerItem>("job_titles.json");
        foreach (var item in items)
        {
            db.JobTitles.Add(new JobTitle(Guid.NewGuid(), item.Name));
        }
    }

    private static async Task SeedEventTypesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.EventTypes.AnyAsync(ct))
        {
            return;
        }

        // Description is shared across legacy seeds — the public frontend
        // doesn't differentiate.
        const string description =
            "الفعاليات التي تقام في مكان وزمان محدد بمشاركة أو حضور جمهور، " +
            "وتتضمن العروض أو الأنشطة التي تهدف للتسلية (مثال: عروض حية)";

        (string name, string slug)[] types =
        {
            ("فعالية ترفيهية", "entertainment"),
            ("فعالية رياضية",  "sports"),
            ("عروض حية",        "live"),
            ("فعالية ثقافية",  "cultural"),
            ("مسرحيات",         "theatre"),
            ("مهرجانات المعارض", "exhibition"),
        };

        foreach (var (name, slug) in types)
        {
            db.EventTypes.Add(new EventType(
                Guid.NewGuid(),
                name,
                description,
                background: $"assets/covers/event_types/{slug}.png",
                icon: $"assets/icons/event_types/{slug}.svg"));
        }
    }

    private static async Task SeedOpportunityCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.OpportunityCategories.AnyAsync(ct))
        {
            return;
        }

        // 7 parent groups, each with its own children. ForVacancy=true marks
        // categories that appear in the individuals (worker) flow — the same
        // distinction the legacy seeder used.
        SeedCategoryGroup(
            db,
            parentTitle: "المواهب",
            parentDescription:
                "خدمة تتيح للأفراد الموهوبين بمشاركة مواهبهم ومهاراتهم في فعاليات الترفيه المختلفة. " +
                "تقوم منصة مطلوب من خلال هذه الخدمة الربط بين مشغلين وملاك المناسبات ،الفعاليات بذوي المواهب " +
                "الراغبين بالمشاركة في احد المناسبات او الفعاليات",
            parentIcon: "assets/icons/opportunity_categories/talents.svg",
            forVacancy: true,
            children: new[]
            {
                ("الغناء", false),
                ("مسرح", false),
                ("موسيقى", false),
                ("كوميديا", false),
                ("الرسم", false),
                ("الفنون والحرف اليدوية", false),
                ("الطهي", false),
                ("التصوير", false),
                ("العروض الحية", false),
                ("الخطابة واللقاء", false),
                ("أخري", true),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "العمل المؤقت",
            parentDescription:
                "خدمة تتيح للافراد الراغبين بالعمل في فرص العمل المؤقتة لدي المنشآت المشاركة في فعاليات الترفيه. " +
                "يتم من خلال خدمات العمل المؤقت علي منصة مطلوب الربط بين الباحثين عن العمل ،الشركات المنظمة أو المشغلة " +
                "للمواسم والمناسبات",
            parentIcon: "assets/icons/opportunity_categories/temporary_work.svg",
            forVacancy: true,
            children: new[]
            {
                ("إدارة الحشود", false),
                ("الخدمات الأمنية", false),
                ("بائع", false),
                ("تنظيم حركة المركبات", false),
                ("خدمة عمال", false),
                ("طاهي", false),
                ("عارض أزياء", false),
                ("محاسب", false),
                ("مرشد", false),
                ("مشغل ألعاب", false),
                ("مقدم مأكولات", false),
                ("مشرف", false),
                ("تحضير القهوة", false),
                ("استقبال", false),
                ("منظم", false),
                ("أخرى", true),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "المأكولات والمشروبات",
            parentDescription:
                "خدمة تتيح لأصحاب الأنشطة التجارية المندرجة تحت مظلة المأكولات والمشروبات المشاركة في فعاليات الترفيه من خلال المنصة",
            parentIcon: "assets/icons/opportunity_categories/food_and_beverages.svg",
            forVacancy: false,
            children: new[]
            {
                ("المطاعم والمقاهي", false),
                ("عربات الطعام المتنقلة", false),
                ("مقدمي المأكولات والمشروبات (الاسر المنتجة)", false),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "المتاجر",
            parentDescription:
                "خدمة تتيح لاصحاب الانشطة التجارية المندرجة تحت مظلة المتاجر المشاركة في الفعاليات الترفيه من خلال المنصة. " +
                "تقوم خدمة المتاجر بالربط بين ملاك الفعاليات والمتاجر والعلامات التجارية الراغبة في المشاركة في المناسبات او الفعاليات.",
            parentIcon: "assets/icons/opportunity_categories/stores.svg",
            forVacancy: false,
            children: new[]
            {
                ("متجر شخصي", false),
                ("متجر", false),
                ("متجر متنقل", false),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "خدمات المساندة",
            parentDescription:
                "خدمة تتيح للمنشآت بالمشاركة من خلال تقديم خدماتها المطلوبة في فعاليات الترفيه. تسمح المنصة من هذه الخدمة " +
                "للشركات في القطاع اللوجيستي او من يمتلك موقع مناسب لاقامة مناسبة بالتسجيل والتعبير عن رغبتهم بالمشاركة " +
                "في موسم او فعالية معينة",
            parentIcon: "assets/icons/opportunity_categories/support_services.svg",
            forVacancy: false,
            children: new[]
            {
                ("خدمات عامة", false),
                ("خدمات لوجستية", false),
                ("خدمات تقنية اداره وتنظيم", false),
                ("مقاولات", false),
                ("شركات التأمين", false),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "المواقع",
            parentDescription:
                "خدمة تتيح لمن يمتلك موقع مناسب من الافراد او المنشآت بالمشاركة بعقار لاقامة فعالية بالتقديم وادراج الموقع الخاص بهم في المنصة",
            parentIcon: "assets/icons/opportunity_categories/land_space.svg",
            forVacancy: false,
            children: new[]
            {
                ("ملعب", false),
                ("مسرح", false),
                ("قاعة", false),
                ("أرض خالية", false),
                ("معرض", false),
            });

        SeedCategoryGroup(
            db,
            parentTitle: "فعاليات الموسم",
            parentDescription:
                "خدمة تتيح للمنشآت المقدمة لخدمات الترفيه بالمشاركة في الفعاليات في مجالات مختلفة",
            parentIcon: "assets/icons/opportunity_categories/season_events.svg",
            forVacancy: false,
            children: new[]
            {
                ("العروض الحية", false),
                ("المهرجانات", false),
                ("المعارض", false),
                ("الأنشطة الترفيهية والرياضية والثقافية", false),
            });
    }

    private static void SeedCategoryGroup(
        AppDbContext db,
        string parentTitle,
        string parentDescription,
        string parentIcon,
        bool forVacancy,
        (string Title, bool IsOther)[] children)
    {
        var parentId = Guid.NewGuid();
        db.OpportunityCategories.Add(new OpportunityCategory(
            parentId,
            parentTitle,
            parentId: null,
            description: parentDescription,
            icon: parentIcon,
            forVacancy: forVacancy));

        foreach (var (title, isOther) in children)
        {
            db.OpportunityCategories.Add(new OpportunityCategory(
                Guid.NewGuid(),
                title,
                parentId: parentId,
                forVacancy: forVacancy,
                isOther: isOther));
        }
    }

    private static async Task SeedOfferCancellationReasonsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.OfferCancellationReasons.AnyAsync(ct))
        {
            return;
        }

        var items = LoadJsonArray<NamedAjeerItem>("cancellation_reasons.json");
        foreach (var item in items)
        {
            // Legacy "أخرى" rows didn't carry an explicit is_other flag in the
            // JSON; infer it from the localized "Other" label so admin tools
            // still find the catch-all.
            db.OfferCancellationReasons.Add(new OfferCancellationReason(
                Guid.NewGuid(),
                item.Name,
                isOther: IsLocalizedOther(item.Name)));
        }
    }

    private static async Task SeedOfferRejectionReasonsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.OfferRejectionReasons.AnyAsync(ct))
        {
            return;
        }

        var items = LoadJsonArray<NamedAjeerItem>("rejection_reasons.json");
        foreach (var item in items)
        {
            db.OfferRejectionReasons.Add(new OfferRejectionReason(
                Guid.NewGuid(),
                item.Name,
                isOther: IsLocalizedOther(item.Name)));
        }
    }

    private static bool IsLocalizedOther(string name)
    {
        var trimmed = name.Trim();
        return trimmed == "أخرى" || trimmed == "أخري" || trimmed == "Other";
    }

    private static async Task SeedSeasonsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Seasons.AnyAsync(ct))
        {
            return;
        }

        const string placeholder =
            "هذا النص هو مثال لنص يمكن أن يستبدل في نفس المساحة، لقد تم توليد هذا النص من مولد النص العربى، " +
            "حيث يمكنك أن تولد مثل هذا النص أو العديد من النصوص الأخرى إضافة إلى زيادة عدد الحروف التى يولدها التطبيق.";

        (string name, string image)[] seasons =
        {
            ("موسم الرياض",   "seasons/ryad.svg"),
            ("موسم الجيمرز",  "seasons/gamers.svg"),
            ("موسم الدرعية", "seasons/dr3ya.svg"),
            ("موسم جدة",      "seasons/gedda.svg"),
            ("موسم الطايف",   "seasons/ta2f.svg"),
        };

        foreach (var (name, image) in seasons)
        {
            db.Seasons.Add(new Season(Guid.NewGuid(), name, image, placeholder));
        }
    }

    private static async Task SeedSuggestedLocationsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.SuggestedLocations.AnyAsync(ct))
        {
            return;
        }

        (string title, decimal lat, decimal lon)[] locations =
        {
            ("الرياض",          24.7136m, 46.6753m),
            ("جدة",             21.2854m, 39.2376m),
            ("مكة المكرمة",     21.3891m, 39.8579m),
            ("المدينة المنورة", 24.5247m, 39.5692m),
            ("الدمام",          26.3927m, 49.9777m),
            ("الخبر",           26.3106m, 50.1974m),
            ("الطائف",          21.4335m, 40.4708m),
            ("الظهران",         26.2361m, 50.0393m),
            ("الجبيل",          27.0064m, 49.6627m),
            ("القطيف",          26.4282m, 50.0997m),
            ("الخرج",           24.5236m, 46.6753m),
            ("الجوف",           29.9677m, 40.2064m),
        };

        foreach (var (title, lat, lon) in locations)
        {
            db.SuggestedLocations.Add(new SuggestedLocation(Guid.NewGuid(), title, lat, lon));
        }
    }

    private static async Task SeedSuggestedAttendeesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.SuggestedAttendees.AnyAsync(ct))
        {
            return;
        }

        (int min, int max)[] ranges =
        {
            (50, 249),
            (250, 499),
            (500, 999),
            (1000, 2500),
        };

        foreach (var (min, max) in ranges)
        {
            db.SuggestedAttendees.Add(new SuggestedAttendee(Guid.NewGuid(), min, max));
        }
    }

    private static async Task SeedSettingsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Settings.AnyAsync(ct))
        {
            return;
        }

        // Same keys as the legacy SettingsSeeder. Values are text — the
        // init-data endpoint sniffs their type on read.
        (string key, string value)[] settings =
        {
            ("ajeer_enabled",             "false"),
            ("minimum_ajeer_contracts",   "20"),
            ("minimum_saudi_contracts",   "50"),
        };

        foreach (var (key, value) in settings)
        {
            db.Settings.Add(new Setting(Guid.NewGuid(), key, value));
        }
    }

    private static async Task SeedTranslationsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Translations.AnyAsync(ct))
        {
            return;
        }

        // Minimal placeholder set — the legacy translations table was empty in
        // the source dump. Seed two locales so the (key, locale) unique index
        // is exercised by integration tests and the init-data endpoint has
        // something to return.
        (string key, string en, string ar)[] entries =
        {
            ("welcome",  "Welcome",  "مرحبا"),
            ("save",     "Save",     "حفظ"),
            ("cancel",   "Cancel",   "إلغاء"),
        };

        foreach (var (key, en, ar) in entries)
        {
            db.Translations.Add(new Translation(Guid.NewGuid(), key, "en", en));
            db.Translations.Add(new Translation(Guid.NewGuid(), key, "ar", ar));
        }
    }

    private static T[] LoadJsonArray<T>(string fileName)
    {
        var assembly = typeof(ReferenceDataSeeder).Assembly;
        var resourceName = $"{ResourceNamespace}.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded seed resource '{resourceName}' not found. Check the .csproj <EmbeddedResource> include.");

        return JsonSerializer.Deserialize<T[]>(stream, SeedJsonOptions)
            ?? throw new InvalidOperationException(
                $"Seed resource '{resourceName}' deserialized to null.");
    }

    private static readonly JsonSerializerOptions SeedJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Wire shape for legacy seed JSON entries that carry the
    /// <c>{ "name": ..., "ajeer_id": ... }</c> pair. We keep <c>AjeerId</c>
    /// in the deserialized form to round-trip the file unchanged but never
    /// persist it.
    /// </summary>
    private sealed class NamedAjeerItem
    {
        public string Name { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ajeer_id")]
        public int? AjeerId { get; init; }
    }

    /// <summary>Wire shape for one region block in <c>saudi_geography.json</c>.</summary>
    private sealed class GeographyRegion
    {
        public string Region { get; init; } = string.Empty;
        public GeographyCity[] Cities { get; init; } = [];
    }

    private sealed class GeographyCity
    {
        public string Name { get; init; } = string.Empty;
        public string[] Districts { get; init; } = [];
    }
}
