using System.Text.Json;
using RemsToRems.Application.Features.PatientLookup;

namespace RemsToRems.Application.Tests.PatientLookup
{
    /// <summary>The JSON body as the API receives it, read without rejecting anything.</summary>
    public sealed class PatientLookupRequestReaderTests
    {
        private static PatientLookupRequest Read(string json)
        {
            using var document = JsonDocument.Parse(json);
            return PatientLookupRequestReader.Read(document.RootElement);
        }

        [Fact]
        public void ContractBody_IsRead()
        {
            var request = Read("""
                { "requestId": "3de6e667-9c77-4ec3-b18e-cdfcc19cae4c",
                  "patient": { "firstName": "Jessica", "lastName": "Rabbit", "dateOfBirth": "1964-08-21", "zipcode": "02003" } }
                """);

            Assert.Equal("3de6e667-9c77-4ec3-b18e-cdfcc19cae4c", request.RequestId);
            Assert.Equal("Jessica", request.Patient!.FirstName);
            Assert.Equal("Rabbit", request.Patient.LastName);
            Assert.Equal("1964-08-21", request.Patient.DateOfBirth);
            Assert.Equal("02003", request.Patient.Zipcode);
            Assert.Empty(request.NonStringFields);
        }

        [Fact]
        public void PropertyNames_AreCaseInsensitive()
        {
            var request = Read("""{ "RequestId": "x", "Patient": { "zipCode": "12345" } }""");

            Assert.Equal("x", request.RequestId);
            Assert.Equal("12345", request.Patient!.Zipcode);
        }

        [Fact]
        public void NullAndMissing_AreMissing_NotInvalid()
        {
            var request = Read("""{ "requestId": null, "patient": { "firstName": null } }""");

            Assert.Null(request.RequestId);
            Assert.Null(request.Patient!.FirstName);
            Assert.Null(request.Patient.Zipcode);
            Assert.Empty(request.NonStringFields);
        }

        [Fact]
        public void NonStringValues_AreRecorded()
        {
            var request = Read("""
                { "requestId": 7, "patient": { "firstName": true, "lastName": ["x"], "dateOfBirth": 19800131, "zipcode": 12345 } }
                """);

            Assert.Equal(new[] { "requestId", "firstName", "lastName", "dateOfBirth", "zipcode" }, request.NonStringFields);
            Assert.Null(request.Patient!.Zipcode);
        }

        [Theory]
        [InlineData("""{ "requestId": "x" }""")]
        [InlineData("""{ "requestId": "x", "patient": null }""")]
        [InlineData("""{ "requestId": "x", "patient": "John Smith" }""")]
        [InlineData("""{ "requestId": "x", "firstName": "John" }""")]   // flat body: patient fields are not read
        public void MissingOrNonObjectPatient_LeavesPatientNull(string json)
        {
            Assert.Null(Read(json).Patient);
        }

        [Fact]
        public void NonObjectBody_HasNoFields()
        {
            var request = Read("""[1, 2]""");

            Assert.Null(request.RequestId);
            Assert.Null(request.Patient);
        }
    }
}
