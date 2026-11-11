# Kratka debugging vježba s Copilotom

Cilj je stvarno upotrijebiti Copilot, razumjeti jedan bug i zabilježiti provjeru. Za razgovor izdvoji približno 5–10 minuta; pokretanje testova može potrajati dulje ako prvi put postavljaš .NET.

Ovo je **kontrolirana vježba s namjerno pogrešnom dodjelom ID-a**. Završni `Program.cs` već koristi ispravan rastući brojač. Ne opisuj vježbu kao raniji stvarni kvar završnog API-ja. Ovaj dokument i pripremljeni prompt još nisu dokaz da je Copilot korišten.

## 1. Otvori Copilot i pošalji ovaj prompt

Možeš koristiti Microsoft Copilot na <https://copilot.microsoft.com/> ili GitHub Copilot Chat u svojem editoru. Zabilježi proizvod koji stvarno koristiš. Kopiraj **cijeli sljedeći blok** u taj alat:

````text
Radim Coursera projekt User Management API u C#-u, ASP.NET Core Minimal API, .NET 10. Ovo je kontrolirana debugging vježba: sljedeća varijanta metode Create namjerno ima pogrešnu dodjelu ID-a. Moj završni projekt već ima zaseban brojač, a ovdje želim razumjeti zašto je on potreban. Ne prikazuj očekivane rezultate kao da sam ih već izmjerio.

Korisnici su u Dictionary<int, User>. POST /api/users poziva Create; uspjeh daje 201 i Location. DELETE briše po ID-u. Vanjski error middleware pretvara neočekivanu iznimku u siguran odgovor 500. Provjera duplikata emaila vraća 409. Sve operacije spremišta koriste isti lock.

Relevantna namjerno pogrešna varijanta:

```csharp
public sealed record User(int Id, string Name, string Email);

public sealed class UserStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, User> _users = new();

    public User? Create(string name, string email)
    {
        lock (_gate)
        {
            if (_users.Values.Any(user =>
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
                return null;

            var user = new User(_users.Count + 1, name, email); // namjerni bug
            _users.Add(user.Id, user);
            return user;
        }
    }

    public bool Delete(int id)
    {
        lock (_gate) return _users.Remove(id);
    }
}
```

Scenarij počinje praznim spremištem i valjanim lokalnim tokenom:
1. POST /api/users s {"name":"Ana Primjer","email":"ana@example.com"}
2. POST /api/users s {"name":"Ivan Primjer","email":"ivan@example.com"}
3. DELETE /api/users/1
4. POST /api/users s {"name":"Mia Primjer","email":"mia@example.com"}
5. GET /api/users/2 i GET /api/users

Želim da četvrti zahtjev uspije s novim jedinstvenim ID-em, a Ivan zadrži ID 2 i svoje podatke.

Objasni vrijednost Count i ključeve dictionaryja nakon svakog koraka. Utvrdi koji redak uzrokuje problem i zašto lock sam ne popravlja tu logičku pogrešku. Predloži najmanju izmjenu s brojačem koji ne ovisi o broju preostalih korisnika. Sačuvaj lock, provjeru emaila i postojeći HTTP ugovor; ne dodaj bazu, pakete ili nove rute. Daj očekivane rezultate istog scenarija prije i poslije izmjene te jedan dodatni rubni test. Objasni i što se događa s brojačem pri ponovnom pokretanju memorijskog API-ja.
````

Sačuvaj stvarni odgovor. Možeš ga kopirati u razgovor u kojem pripremaš projekt kako bi se popravak i provjere dovršili prema tom odgovoru. Nemoj slati lokalni token.

## 2. Provjeri što Copilot predlaže

Trebao bi moći objasniti sljedeće: nakon stvaranja ID-eva 1 i 2 te brisanja ID-a 1, preostaje jedan zapis s ključem 2. Izraz `Count + 1` opet daje 2 pa `Dictionary.Add` odbija postojeći ključ. U ovom API-ju to bi se pretvorilo u 500. To je očekivanje koje treba potvrditi izvođenjem.

Najmanji očekivani popravak je polje `private int _nextId = 1;` i stvaranje korisnika s `new User(_nextId++, name, email)` unutar postojećeg zaključavanja. U završnom projektu ti redci već postoje. U evidenciji jasno navedi da je Copilot pomogao analizirati i popraviti namjerno pogrešnu varijantu.

## 3. Izvedi provjeru prije i poslije

Ako provjeravaš sam, radi u zasebnoj lokalnoj kopiji izvornog projekta, izvan mape završne predaje. U kopiji promijeni samo redak dodjele ID-a u `_users.Count + 1`; pokretanje i token postavi prema README-ju. Za svaki prolaz pokreni novi proces s praznim spremištem, a u HTTP klijentu šalji valjani token i `Content-Type: application/json` za POST.

Pošalji pet zahtjeva iz prompta. Zapiši stvarne statuse i tijela. Zaustavi proces, primijeni pregledani Copilotov popravak, ponovno pokreni API i ponovi iste zahtjeve.

| Korak | Očekivanje s namjernim bugom | Očekivanje nakon popravka |
| --- | --- | --- |
| POST Ana | 201, ID 1 | 201, ID 1 |
| POST Ivan | 201, ID 2 | 201, ID 2 |
| DELETE 1 | 204 | 204 |
| POST Mia | 500 zbog ponovljenog ključa 2 | 201, ID 3, Location `/api/users/3` |
| GET 2 | 200, Ivan nepromijenjen | 200, Ivan nepromijenjen |
| GET lista | 200, samo Ivan | 200, Ivan i Mia |

Dodatno pošalji POST s novim imenom i emailom `IVAN@example.com`. Očekuje se 409 bez promjene spremljenih korisnika. Tablica prikazuje očekivanja, **ne već izmjerene rezultate**.

## 4. Dovrši evidenciju

U [COPILOT_DEBUGGING.md](COPILOT_DEBUGGING.md) upiši stvarni datum, naziv proizvoda, prompt, sažetak dobivenog odgovora, što si prihvatio i stvarne rezultate. Ako je testove pokrenuo drugi alat ili pomoćnik, jasno navedi tu podjelu rada. Dodaj commit koji sadrži dovršenu evidenciju. Namjerno pogrešnu varijantu nemoj zamijeniti za završni `Program.cs`.

Kriterij još nije potvrđen dok stvarno ne upotrijebiš Copilot i ne dovršiš zapis. Razgovor s drugim AI alatom nemoj nazvati Copilot sesijom.
