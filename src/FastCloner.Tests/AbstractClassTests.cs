using System.Linq;
using FastCloner.SourceGenerator.Shared;
using System.Threading.Tasks;

namespace FastCloner.Tests;

// External derived types (simulating types from another assembly or not auto-discovered)
public class ExternalDog : AbstractClassTests.Pet
{
    public string? Breed { get; set; }
}

public class ExternalCat : AbstractClassTests.Pet
{
    public string? FurColor { get; set; }
}

public class ExternalBird : AbstractClassTests.Pet
{
    public double Wingspan { get; set; }
    public bool CanFly { get; set; }
}
[SourceGeneratorCompatible]
public class AbstractClassTests
{
    #region Test Classes - Abstract Base with [FastClonerInclude]
    
    /// <summary>
    /// Abstract base class that uses [FastClonerInclude] to register external derived types.
    /// Auto-discovery is disabled to ensure only explicitly included types are used.
    /// </summary>
    [FastClonerClonable]
    [FastClonerDisableAutoDiscovery]
    [FastClonerInclude(typeof(ExternalDog), typeof(ExternalCat), typeof(ExternalBird))]
    public abstract class Pet
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    #endregion

    #region Test Classes - Abstract Base with Same Assembly Derived Types
    
    [FastClonerClonable]
    public abstract class Animal
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    public class Dog : Animal
    {
        public string? Breed { get; set; }
        public bool IsTrained { get; set; }
    }

    public class Cat : Animal
    {
        public string? Color { get; set; }
        public bool IsIndoor { get; set; }
    }

    #endregion

    #region Test Classes - Abstract Base with Collections
    
    [FastClonerClonable]
    public abstract class Vehicle
    {
        public string? Brand { get; set; }
        public List<string>? Features { get; set; }
    }

    public class Car : Vehicle
    {
        public int NumberOfDoors { get; set; }
        public bool IsElectric { get; set; }
    }

    public class Motorcycle : Vehicle
    {
        public bool HasSidecar { get; set; }
    }

    #endregion

    #region Test Classes - Multi-level Inheritance
    
    [FastClonerClonable]
    public abstract class Shape
    {
        public string? Name { get; set; }
    }

    public abstract class Polygon : Shape
    {
        public int NumberOfSides { get; set; }
    }

    public class Triangle : Polygon
    {
        public double Base { get; set; }
        public double Height { get; set; }
        
        public Triangle()
        {
            NumberOfSides = 3;
        }
    }

    public class Rectangle : Polygon
    {
        public double Width { get; set; }
        public double Length { get; set; }
        
        public Rectangle()
        {
            NumberOfSides = 4;
        }
    }

    #endregion

    #region Test Classes - Abstract with Nested Complex Types
    
    [FastClonerClonable]
    public abstract class Employee
    {
        public string? Name { get; set; }
        public Address? HomeAddress { get; set; }
    }

    public class Manager : Employee
    {
        public List<string>? DirectReports { get; set; }
        public string? Department { get; set; }
    }

    public class Developer : Employee
    {
        public List<string>? ProgrammingLanguages { get; set; }
        public string? Team { get; set; }
    }

    public class Address
    {
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? ZipCode { get; set; }
    }

    #endregion

    #region Test Classes - Abstract Base with Clonable Derived Types

    // Both the abstract base and the derived types carry [FastClonerClonable].
    // The abstract dispatcher must delegate to each derived type's own generated cloner.
    [FastClonerClonable]
    public abstract class Creature
    {
        public string? Name { get; set; }
    }

    [FastClonerClonable]
    public class Puppy : Creature
    {
        public string? Bark { get; set; }
    }

    [FastClonerClonable]
    public class Kitten : Creature
    {
        public string? Meow { get; set; }
    }

    #endregion

    #region Tests - Basic Abstract Class Cloning
    
    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_Dog_Should_Clone_Correctly()
    {
        // Arrange
        Dog dog = new Dog
        {
            Name = "Buddy",
            Age = 5,
            Breed = "Golden Retriever",
            IsTrained = true
        };
        
        // Act - Clone via abstract base type
        Animal animal = dog;
        Animal? clone = animal.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Dog>();
        await Assert.That(clone).IsNotSameReferenceAs(dog);

        Dog clonedDog = (Dog)clone!;
        await Assert.That(clonedDog.Name).IsEqualTo("Buddy");
        await Assert.That(clonedDog.Age).IsEqualTo(5);
        await Assert.That(clonedDog.Breed).IsEqualTo("Golden Retriever");
        await Assert.That(clonedDog.IsTrained).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_Cat_Should_Clone_Correctly()
    {
        // Arrange
        Cat cat = new Cat
        {
            Name = "Whiskers",
            Age = 3,
            Color = "Orange",
            IsIndoor = true
        };
        
        // Act - Clone via abstract base type
        Animal animal = cat;
        Animal? clone = animal.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Cat>();
        await Assert.That(clone).IsNotSameReferenceAs(cat);

        Cat clonedCat = (Cat)clone!;
        await Assert.That(clonedCat.Name).IsEqualTo("Whiskers");
        await Assert.That(clonedCat.Age).IsEqualTo(3);
        await Assert.That(clonedCat.Color).IsEqualTo("Orange");
        await Assert.That(clonedCat.IsIndoor).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_With_ClonableDerived_Should_Dispatch_To_Derived_Clone()
    {
        // Arrange - derived types have their own [FastClonerClonable], so the abstract
        // dispatcher delegates to their generated cloners instead of inline helpers
        Puppy puppy = new Puppy { Name = "Rex", Bark = "Woof" };
        Kitten kitten = new Kitten { Name = "Mia", Meow = "Purr" };

        // Act - Clone via abstract base type
        Creature creature = puppy;
        Creature? puppyClone = creature.FastDeepClone();
        Creature? kittenClone = ((Creature)kitten).FastDeepClone();

        // Assert - runtime type is preserved and members are copied
        await Assert.That(puppyClone).IsNotNull();
        await Assert.That(puppyClone).IsTypeOf<Puppy>();
        await Assert.That(puppyClone).IsNotSameReferenceAs(puppy);
        await Assert.That(((Puppy)puppyClone!).Name).IsEqualTo("Rex");
        await Assert.That(((Puppy)puppyClone).Bark).IsEqualTo("Woof");

        await Assert.That(kittenClone).IsNotNull();
        await Assert.That(kittenClone).IsTypeOf<Kitten>();
        await Assert.That(((Kitten)kittenClone!).Name).IsEqualTo("Mia");
        await Assert.That(((Kitten)kittenClone).Meow).IsEqualTo("Purr");
    }

    #endregion

    #region Tests - Abstract with Collections
    
    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_Vehicle_With_Collection_Should_Deep_Clone()
    {
        // Arrange
        Car car = new Car
        {
            Brand = "Tesla",
            Features = ["Autopilot", "Electric", "Touchscreen"],
            NumberOfDoors = 4,
            IsElectric = true
        };
        
        // Act
        Vehicle vehicle = car;
        Vehicle? clone = vehicle.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Car>();
        await Assert.That(clone!.Features).IsNotSameReferenceAs(car.Features);

        Car clonedCar = (Car)clone;
        await Assert.That(clonedCar.Brand).IsEqualTo("Tesla");
        await Assert.That(clonedCar.NumberOfDoors).IsEqualTo(4);
        await Assert.That(clonedCar.IsElectric).IsTrue();
        await Assert.That(clonedCar.Features!.Count).IsEqualTo(3);

        // Verify deep clone - modify original
        car.Features![0] = "Manual";
        await Assert.That(clonedCar.Features[0]).IsEqualTo("Autopilot"); // Clone unchanged
    }

    #endregion

    #region Tests - Multi-level Inheritance
    
    [Test]
    [SourceGeneratorCompatible]
    public async Task MultiLevel_Triangle_Should_Clone_Via_Shape()
    {
        // Arrange
        Triangle triangle = new Triangle
        {
            Name = "Equilateral",
            Base = 10.0,
            Height = 8.66
        };
        
        // Act - Clone via most abstract type
        Shape shape = triangle;
        Shape? clone = shape.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Triangle>();

        Triangle clonedTriangle = (Triangle)clone!;
        await Assert.That(clonedTriangle.Name).IsEqualTo("Equilateral");
        await Assert.That(clonedTriangle.NumberOfSides).IsEqualTo(3);
        await Assert.That(clonedTriangle.Base).IsEqualTo(10.0);
        await Assert.That(clonedTriangle.Height).IsEqualTo(8.66);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task MultiLevel_Rectangle_Should_Clone_Via_Shape()
    {
        // Arrange
        Rectangle rectangle = new Rectangle
        {
            Name = "Square",
            Width = 5.0,
            Length = 5.0
        };
        
        // Act
        Shape shape = rectangle;
        Shape? clone = shape.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Rectangle>();

        Rectangle clonedRect = (Rectangle)clone!;
        await Assert.That(clonedRect.Name).IsEqualTo("Square");
        await Assert.That(clonedRect.NumberOfSides).IsEqualTo(4);
        await Assert.That(clonedRect.Width).IsEqualTo(5.0);
        await Assert.That(clonedRect.Length).IsEqualTo(5.0);
    }

    #endregion

    #region Tests - Abstract with Complex Nested Types
    
    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_Employee_With_Complex_Types_Should_Deep_Clone()
    {
        // Arrange
        Manager manager = new Manager
        {
            Name = "Alice",
            HomeAddress = new Address
            {
                Street = "123 Main St",
                City = "Seattle",
                ZipCode = "98101"
            },
            DirectReports = ["Bob", "Charlie", "Diana"],
            Department = "Engineering"
        };
        
        // Act
        Employee employee = manager;
        Employee? clone = employee.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Manager>();

        Manager clonedManager = (Manager)clone!;
        await Assert.That(clonedManager.Name).IsEqualTo("Alice");
        await Assert.That(clonedManager.Department).IsEqualTo("Engineering");

        // Verify deep clone of nested object
        await Assert.That(clonedManager.HomeAddress).IsNotNull();
        await Assert.That(clonedManager.HomeAddress).IsNotSameReferenceAs(manager.HomeAddress);
        await Assert.That(clonedManager.HomeAddress!.Street).IsEqualTo("123 Main St");
        await Assert.That(clonedManager.HomeAddress.City).IsEqualTo("Seattle");

        // Verify deep clone of list
        await Assert.That(clonedManager.DirectReports).IsNotNull();
        await Assert.That(clonedManager.DirectReports).IsNotSameReferenceAs(manager.DirectReports);
        await Assert.That(clonedManager.DirectReports!.Count).IsEqualTo(3);

        // Verify independence
        manager.HomeAddress!.City = "Portland";
        await Assert.That(clonedManager.HomeAddress.City).IsEqualTo("Seattle"); // Clone unchanged
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_Developer_Should_Clone_Correctly()
    {
        // Arrange
        Developer developer = new Developer
        {
            Name = "Bob",
            HomeAddress = new Address
            {
                Street = "456 Oak Ave",
                City = "San Francisco",
                ZipCode = "94102"
            },
            ProgrammingLanguages = ["C#", "Python", "TypeScript"],
            Team = "Backend"
        };
        
        // Act
        Employee employee = developer;
        Employee? clone = employee.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Developer>();

        Developer clonedDev = (Developer)clone!;
        await Assert.That(clonedDev.Name).IsEqualTo("Bob");
        await Assert.That(clonedDev.Team).IsEqualTo("Backend");
        await Assert.That(clonedDev.ProgrammingLanguages!.Count).IsEqualTo(3);
        await Assert.That(clonedDev.ProgrammingLanguages).Contains("C#");
    }

    #endregion

    #region Tests - [FastClonerInclude] for External Derived Types

    [Test]
    [SourceGeneratorCompatible]
    public async Task FastClonerInclude_ExternalDog_Should_Clone()
    {
        // Arrange
        ExternalDog dog = new ExternalDog
        {
            Name = "Rex",
            Age = 4,
            Breed = "German Shepherd"
        };
        
        // Act - Clone via abstract base type
        Pet pet = dog;
        Pet? clone = pet.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<ExternalDog>();
        await Assert.That(clone).IsNotSameReferenceAs(dog);

        ExternalDog clonedDog = (ExternalDog)clone!;
        await Assert.That(clonedDog.Name).IsEqualTo("Rex");
        await Assert.That(clonedDog.Age).IsEqualTo(4);
        await Assert.That(clonedDog.Breed).IsEqualTo("German Shepherd");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task FastClonerInclude_ExternalCat_Should_Clone()
    {
        // Arrange
        ExternalCat cat = new ExternalCat
        {
            Name = "Mittens",
            Age = 2,
            FurColor = "Calico"
        };
        
        // Act
        Pet pet = cat;
        Pet? clone = pet.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<ExternalCat>();

        ExternalCat clonedCat = (ExternalCat)clone!;
        await Assert.That(clonedCat.Name).IsEqualTo("Mittens");
        await Assert.That(clonedCat.Age).IsEqualTo(2);
        await Assert.That(clonedCat.FurColor).IsEqualTo("Calico");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task FastClonerInclude_ExternalBird_Should_Clone()
    {
        // Arrange
        ExternalBird bird = new ExternalBird
        {
            Name = "Tweety",
            Age = 1,
            Wingspan = 0.3,
            CanFly = true
        };
        
        // Act
        Pet pet = bird;
        Pet clone = pet.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<ExternalBird>();

        ExternalBird clonedBird = (ExternalBird)clone!;
        await Assert.That(clonedBird.Name).IsEqualTo("Tweety");
        await Assert.That(clonedBird.Age).IsEqualTo(1);
        await Assert.That(clonedBird.Wingspan).IsEqualTo(0.3);
        await Assert.That(clonedBird.CanFly).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task FastClonerInclude_Multiple_Types_Independence()
    {
        // Arrange - create instances of all included types
        ExternalDog dog = new ExternalDog { Name = "Buddy", Age = 3, Breed = "Labrador" };
        ExternalCat cat = new ExternalCat { Name = "Whiskers", Age = 5, FurColor = "Orange" };
        ExternalBird bird = new ExternalBird { Name = "Polly", Age = 2, Wingspan = 0.5, CanFly = true };
        
        // Act - Clone all via abstract base
        Pet[] pets = [dog, cat, bird];
        Pet?[] clones = pets.Select(p => p.FastDeepClone()).ToArray();
        
        // Assert - Each clone is correct type and independent
        await Assert.That(clones[0]).IsTypeOf<ExternalDog>();
        await Assert.That(clones[1]).IsTypeOf<ExternalCat>();
        await Assert.That(clones[2]).IsTypeOf<ExternalBird>();

        // Verify independence - modify originals
        dog.Name = "Changed";
        cat.FurColor = "Black";
        bird.CanFly = false;
        
        await Assert.That(((ExternalDog)clones[0]!).Name).IsEqualTo("Buddy");
        await Assert.That(((ExternalCat)clones[1]!).FurColor).IsEqualTo("Orange");
        await Assert.That(((ExternalBird)clones[2]!).CanFly).IsTrue();
    }

    #endregion

    #region Tests - Null Handling
    
    [Test]
    [SourceGeneratorCompatible]
    public async Task Null_Abstract_Should_Return_Null()
    {
        // Arrange
        Animal? animal = null;
        
        // Act
        Animal? clone = animal.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Abstract_With_Null_Properties_Should_Clone()
    {
        // Arrange
        Dog dog = new Dog
        {
            Name = null,
            Age = 2,
            Breed = null,
            IsTrained = false
        };
        
        // Act
        Animal animal = dog;
        Animal? clone = animal.FastDeepClone();
        
        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Dog>();

        Dog clonedDog = (Dog)clone!;
        await Assert.That(clonedDog.Name).IsNull();
        await Assert.That(clonedDog.Age).IsEqualTo(2);
        await Assert.That(clonedDog.Breed).IsNull();
        await Assert.That(clonedDog.IsTrained).IsFalse();
    }

    #endregion
}
